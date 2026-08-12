using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoToolbox.Core.Auth;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// CE-to-F&amp;O Virtual Tables (#23): a read-only inspector over the Dataverse environment's virtual
/// (external) tables, focused on the finance-and-operations-backed ones (the <c>mserp_</c> provider). It
/// lists each table's logical/display/external name, data source &amp; provider, and managed state so an
/// architect can verify CE-to-F&amp;O virtual-table setup without bouncing through admin screens. This is
/// deliberately distinct from the Dual-Write Map Browser (which inspects data-copy maps). No mutations —
/// virtual tables are generated/configured in the maker portal, linked here via "Open in Dataverse".
/// </summary>
public partial class VirtualTablesViewModel : ObservableObject
{
    private readonly IVirtualTableReader _reader;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly IUrlLauncher _launcher;
    // Whether any load has completed (drives the empty state, and separates "never loaded" from
    // "loaded while no environment was active" — both have a null environment stamp).
    private bool _loaded;
    // The environment the listed tables were loaded from. The shell can switch the active environment under
    // this cached VM (the "Refresh open tools?" prompt is declinable) while SelectedTableUrl resolves the
    // ACTIVE environment at click time — so a stale list would deep-link "Open in Dataverse" into a
    // different environment than the one whose tables are on screen. Re-stamped by each successful load.
    private string? _loadedEnvId;

    public ObservableCollection<VirtualTableInfo> Tables { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    [NotifyPropertyChangedFor(nameof(NoSearchMatches))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTableUrl))]
    [NotifyPropertyChangedFor(nameof(HasSelectionLink))]
    [NotifyCanExecuteChangedFor(nameof(OpenInDataverseCommand))]
    private VirtualTableInfo? _selectedTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string _loadError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;

    /// <summary>How many non-F&amp;O virtual tables exist (shown as context; this screen lists only F&amp;O ones).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherVirtual))]
    private int _otherVirtualCount;

    /// <summary>
    /// Name of the environment the listed tables were loaded from, so the header states which environment
    /// the grid (and therefore the "Open in Dataverse" link) belongs to. Empty when nothing is listed.
    /// </summary>
    [ObservableProperty]
    private string _loadedEnvName = string.Empty;

    public bool HasOtherVirtual => OtherVirtualCount > 0;

    public VirtualTablesViewModel(IVirtualTableReader reader, Func<EnvProfile?>? activeEnv = null, IUrlLauncher? launcher = null)
    {
        _reader = reader;
        _activeEnv = activeEnv ?? (() => null);
        _launcher = launcher ?? new FakeUrlLauncher();
        Tables.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTables));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(NoSearchMatches));
        };
    }

    public bool HasTables => Tables.Count > 0;
    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);
    public bool HasSelection => SelectedTable is not null;
    public bool ShowEmptyState => _loaded && !IsLoading && !HasLoadError && Tables.Count == 0;

    /// <summary>True when tables are loaded but the current search hides all of them (vs none loaded at all).</summary>
    public bool NoSearchMatches => HasTables && !string.IsNullOrWhiteSpace(Search) && !Filtered.Any();

    public IEnumerable<VirtualTableInfo> Filtered =>
        string.IsNullOrWhiteSpace(Search)
            ? Tables
            : Tables.Where(t =>
                t.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                t.LogicalName.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                t.ExternalName.Contains(Search, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deep link to the selected table's list view in the model-driven app, or null if unbuildable.</summary>
    public string? SelectedTableUrl => BuildListUrl(_activeEnv()?.DataverseUrl, SelectedTable?.LogicalName);

    public bool HasSelectionLink => SelectedTableUrl is not null;

    // Loads on first activation AND after an environment switch — deliberately not a one-shot. The shell
    // keeps this VM alive across a declined "Refresh open tools?" prompt, so re-activating under a different
    // environment must reload; otherwise the grid keeps environment A's tables while the deep link (which
    // resolves the ACTIVE environment) points into environment B.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        if (!_loaded || EnvChangedSinceLoad())
        {
            await ReloadAsync(ct);
        }
    }

    // True when the active environment moved on since the listed tables were loaded.
    private bool EnvChangedSinceLoad() =>
        !string.Equals(_activeEnv()?.Id, _loadedEnvId, StringComparison.Ordinal);

    [RelayCommand]
    private Task Refresh(CancellationToken ct) => ReloadAsync(ct);

    private async Task ReloadAsync(CancellationToken ct)
    {
        // Captured BEFORE the read (same pattern as DualWriteMapViewModel.LoadMapsAsync): the reader resolves
        // the active environment internally at call time, so a switch landing mid-load must not stamp
        // environment B onto the tables that were actually read from environment A — that would make the next
        // activation a no-op and leave the grid permanently mismatched with the deep link. Capturing first
        // errs the safe way: a mid-load switch leaves stamp = A while active = B, so the next Initialize
        // reloads. The residual window is the microseconds between this line and the reader resolving the
        // environment, which is as atomic as this seam allows without plumbing it through the reader API.
        var env = _activeEnv();
        var envId = env?.Id;
        var envName = env?.Name ?? string.Empty;
        IsLoading = true;
        LoadError = string.Empty;
        try
        {
            var result = await _reader.GetVirtualTablesAsync(ct);
            _loaded = true;
            if (!result.IsSuccess)
            {
                Tables.Clear();
                OtherVirtualCount = 0;
                // Nothing is listed, so there's no environment to label. The id stamp is left alone: a
                // failure under a new environment should still reload on the next activation.
                LoadedEnvName = string.Empty;
                LoadError = result.Error ?? "Failed to load virtual tables.";
                return;
            }

            OtherVirtualCount = result.Tables.Count(t => !t.IsFinanceAndOperations);
            var fo = result.Tables
                .Where(t => t.IsFinanceAndOperations)
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Tables.Clear();
            foreach (var table in fo)
            {
                Tables.Add(table);
            }

            // Stamp what these tables belong to — the environment captured before the read, not whatever is
            // active now.
            _loadedEnvId = envId;
            LoadedEnvName = envName;
            OnPropertyChanged(nameof(Filtered));
        }
        catch (OperationCanceledException)
        {
            // Cancellation just leaves the prior list in place.
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    // Opens the selected virtual table's list view in the model-driven app (read-only inspection).
    [RelayCommand(CanExecute = nameof(HasSelectionLink))]
    private async Task OpenInDataverse()
    {
        if (SelectedTableUrl is { } url)
        {
            await _launcher.OpenAsync(url);
        }
    }

    private static string? BuildListUrl(string? dataverseUrl, string? logicalName)
    {
        if (string.IsNullOrWhiteSpace(dataverseUrl) || string.IsNullOrWhiteSpace(logicalName))
        {
            return null;
        }

        var baseUrl = ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(dataverseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl;
        }

        return $"{baseUrl}/main.aspx?pagetype=entitylist&etn={Uri.EscapeDataString(logicalName)}";
    }
}
