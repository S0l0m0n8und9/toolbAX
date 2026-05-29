using FoToolbox.Core.DualWrite;
using FoToolbox.Core.Profiles;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DualWriteComparePlugin;

/// <summary>
/// Compares dual-write map configuration between two environments. Read-only: it resolves
/// and lists maps from each gateway connection, then diffs them by name
/// (presence / active version / state) via <see cref="DualWriteMapComparer"/>.
/// </summary>
public sealed class DualWriteCompareViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly DualWriteConnectionStore _store;
    private readonly IDualWriteGatewayFactory _factory;

    private string _statusMessage = "Configure both environments, then Compare.";
    private string _summaryMessage = string.Empty;
    private bool _isBusy;
    private bool _showOnlyDifferences;

    internal DualWriteCompareViewModel(IPluginContext ctx, DualWriteConnectionStore store, IDualWriteGatewayFactory factory)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        Left = new ConnectionEditorViewModel("Left", "Environment A (left)");
        Right = new ConnectionEditorViewModel("Right", "Environment B (right)");

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        Action<Exception> onError = ex =>
        {
            _ctx.Logger.LogError(ex, "DualWriteCompare command failed.");
            StatusMessage = $"Command failed: {ex.Message}";
            IsBusy = false;
        };

        SaveLeftCommand = new AsyncRelayCommand(ct => SaveSideAsync(Left, ct), onError);
        SaveRightCommand = new AsyncRelayCommand(ct => SaveSideAsync(Right, ct), onError);
        CompareCommand = new AsyncRelayCommand(CompareAsync, onError);

        _ = InitializeAsync();
    }

    public DualWriteCompareViewModel(IPluginContext ctx)
        : this(ctx, new DualWriteConnectionStore(ProfilePaths.ResolveAppDataPath("dualwrite-compare-connections.json")), new DualWriteGatewayFactory())
    {
    }

    public ConnectionEditorViewModel Left { get; }
    public ConnectionEditorViewModel Right { get; }
    public ObservableCollection<DualWriteMapComparisonRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    public AsyncRelayCommand SaveLeftCommand { get; }
    public AsyncRelayCommand SaveRightCommand { get; }
    public AsyncRelayCommand CompareCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SummaryMessage
    {
        get => _summaryMessage;
        set { _summaryMessage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); } }
    }

    public bool IsNotBusy => !IsBusy;

    public bool ShowOnlyDifferences
    {
        get => _showOnlyDifferences;
        set { if (_showOnlyDifferences != value) { _showOnlyDifferences = value; OnPropertyChanged(); RowsView.Refresh(); } }
    }

    private bool FilterRow(object? item) =>
        !ShowOnlyDifferences || (item is DualWriteMapComparisonRow row && row.IsDifference);

    private async Task InitializeAsync()
    {
        try
        {
            await LoadSideAsync(Left, defaultIdentifier: _ctx.CurrentEnv.BaseUrl);
            await LoadSideAsync(Right, defaultIdentifier: string.Empty);
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed loading saved compare connections.");
        }
    }

    private async Task LoadSideAsync(ConnectionEditorViewModel editor, string defaultIdentifier)
    {
        var settings = await _store.GetAsync(editor.Key, CancellationToken.None);
        editor.GatewayBaseUrl = settings.GatewayBaseUrl;
        editor.FoIdentifier = string.IsNullOrWhiteSpace(settings.FoIdentifier) ? defaultIdentifier : settings.FoIdentifier;
        editor.Summary = settings.IsComplete ? $"Configured: {settings.GatewayBaseUrl}" : "Not configured.";
    }

    private async Task SaveSideAsync(ConnectionEditorViewModel editor, CancellationToken ct)
    {
        var settings = await BuildSettingsAsync(editor, ct);
        await _store.SaveAsync(settings, ct);
        editor.BearerToken = string.Empty;
        editor.Summary = settings.IsComplete ? $"Saved: {settings.GatewayBaseUrl}" : "Saved (incomplete — needs URL, identifier, token).";
        StatusMessage = $"{editor.Title} connection saved.";
    }

    private async Task<DualWriteConnectionSettings> BuildSettingsAsync(ConnectionEditorViewModel editor, CancellationToken ct)
    {
        // Preserve a stored token when the box is blank so users can re-compare without re-pasting.
        var token = editor.BearerToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            var existing = await _store.GetAsync(editor.Key, ct);
            token = existing.BearerToken ?? string.Empty;
        }

        return new DualWriteConnectionSettings(
            editor.Key,
            editor.GatewayBaseUrl?.Trim() ?? string.Empty,
            editor.FoIdentifier?.Trim() ?? string.Empty,
            token);
    }

    private async Task CompareAsync(CancellationToken ct)
    {
        var left = await BuildSettingsAsync(Left, ct);
        var right = await BuildSettingsAsync(Right, ct);
        if (!left.IsComplete || !right.IsComplete)
        {
            StatusMessage = "Both environments need a gateway URL, F&O identifier and bearer token.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Loading maps from both environments...";
            var leftMaps = await LoadMapsAsync(left, ct);
            var rightMaps = await LoadMapsAsync(right, ct);

            var rows = DualWriteMapComparer.Compare(leftMaps, rightMaps);
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
            }

            RowsView.Refresh();

            var differences = rows.Count(r => r.IsDifference);
            var onlyLeft = rows.Count(r => r.Verdict == DualWriteComparisonVerdict.OnlyInLeft);
            var onlyRight = rows.Count(r => r.Verdict == DualWriteComparisonVerdict.OnlyInRight);
            var versionDiffs = rows.Count(r => r.Verdict == DualWriteComparisonVerdict.VersionMismatch);
            var stateDiffs = rows.Count(r => r.Verdict == DualWriteComparisonVerdict.StateMismatch);
            SummaryMessage = $"{rows.Count} map(s): {differences} difference(s) — {onlyLeft} only-left, {onlyRight} only-right, {versionDiffs} version, {stateDiffs} state.";
            StatusMessage = differences == 0 ? "Environments match." : "Comparison complete.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<System.Collections.Generic.IReadOnlyList<DualWriteMap>> LoadMapsAsync(DualWriteConnectionSettings settings, CancellationToken ct)
    {
        var gateway = _factory.Create(settings);
        var env = await gateway.GetEnvironmentAsync(settings.FoIdentifier, ct);
        if (string.IsNullOrWhiteSpace(env.Cid))
        {
            throw new InvalidOperationException($"Could not resolve a connection id for identifier '{settings.FoIdentifier}'.");
        }

        return await gateway.GetMapsAsync(env.Cid, ct);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
