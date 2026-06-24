using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>A lifecycle action the command bar can run (label + gateway action type + danger note).</summary>
public sealed record OpsAction(string Label, DualWriteActionType Type, bool Danger, string? Caveat);

/// <summary>
/// Dual-Write Operations screen (control-map §3). Connects to the live dual-write gateway for the active
/// environment (<see cref="IDualWriteConnector"/> over the real FoToolbox.Core gateway), lists its maps,
/// and runs lifecycle actions: select map(s) → confirm → <c>StartActionAsync</c> → poll
/// <c>GetStatusAsync</c> until terminal → refresh. Actions are gateway-validated (no client-side state
/// eligibility); the screen gates only on a connection + a selection.
/// </summary>
public partial class DualWriteOpsViewModel : ObservableObject, IDisposable
{
    private readonly IDualWriteConnector _connector;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly IDialogService _dialogs;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _actionTimeout;
    // Debug mode is a finance-and-operations OData write (DualWriteProjectConfiguration.IsDebugMode),
    // separate from the gateway lifecycle actions — hence the F&O OData client + $metadata resolver.
    private readonly IODataClient _odata;
    private readonly IMetadataService _metadata;
    private DualWriteSession? _session;

    public ObservableCollection<MapRowViewModel> Maps { get; } = new();

    public IReadOnlyList<OpsAction> Actions { get; } = new[]
    {
        new OpsAction("Start", DualWriteActionType.Start, Danger: false, Caveat: null),
        new OpsAction("Stop", DualWriteActionType.Stop, Danger: true, Caveat: "This halts replication for the selected maps."),
        new OpsAction("Pause", DualWriteActionType.Pause, Danger: false, Caveat: null),
        new OpsAction("Resume", DualWriteActionType.Resume, Danger: false, Caveat: null),
        new OpsAction("Initial sync", DualWriteActionType.InitialSync, Danger: true, Caveat: "Initial sync re-synchronises all data and can be long-running."),
    };

    // Named actions so each command-bar button binds RunActionCommand with its own parameter. Looked up
    // by type (not index) so reordering the Actions list can't silently swap which button does what.
    public OpsAction StartAction => Action(DualWriteActionType.Start);
    public OpsAction StopAction => Action(DualWriteActionType.Stop);
    public OpsAction PauseAction => Action(DualWriteActionType.Pause);
    public OpsAction ResumeAction => Action(DualWriteActionType.Resume);
    public OpsAction InitialAction => Action(DualWriteActionType.InitialSync);

    private OpsAction Action(DualWriteActionType type) => Actions.Single(a => a.Type == type);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableDebugForSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableDebugForSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableDebugForSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableDebugForSelectedCommand))]
    private string? _connectionName;

    [ObservableProperty]
    private string _status = "Not connected.";

    [ObservableProperty]
    private string? _loadError;

    /// <summary>Outcome of the last debug-mode toggle (empty until one is attempted).</summary>
    [ObservableProperty]
    private string _debugStatus = string.Empty;

    public bool IsConnected => ConnectionName is not null;

    public bool HasMaps => Maps.Count > 0;

    public int SelectedCount => Maps.Count(m => m.IsSelected);

    public DualWriteOpsViewModel(
        IDualWriteConnector connector,
        Func<EnvProfile?> activeEnv,
        IDialogService dialogs,
        TimeSpan? pollInterval = null,
        TimeSpan? actionTimeout = null,
        IODataClient? odata = null,
        IMetadataService? metadata = null)
    {
        _connector = connector;
        _activeEnv = activeEnv;
        _dialogs = dialogs;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(600);
        // Safety net: never poll a stuck request forever (a hung gateway worker would otherwise lock the UI).
        _actionTimeout = actionTimeout ?? TimeSpan.FromSeconds(120);
        _odata = odata ?? new FakeODataClient();
        _metadata = metadata ?? new FakeMetadataService();
        Maps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMaps));
    }

    private bool CanLoad() => !IsBusy;

    /// <summary>Connects to the gateway for the active environment and loads its maps.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanLoad))]
    private async Task Load(CancellationToken ct)
    {
        var env = _activeEnv();
        if (env is null)
        {
            LoadError = "Select an environment first.";
            Status = "No active environment.";
            return;
        }

        IsBusy = true;
        LoadError = null;
        Status = "Connecting…";
        try
        {
            var session = await _connector.ConnectAsync(env, ct);
            DisposeSession();
            _session = session;
            ConnectionName = session.Cname;

            var maps = await session.Gateway.GetMapsAsync(session.Cid, ct);
            PopulateMaps(maps, keepSelectedIds: null);
            Status = Maps.Count == 0 ? "No maps on this connection." : $"{Maps.Count} map(s).";
        }
        catch (OperationCanceledException)
        {
            ResetDisconnected("Cancelled.");
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            ResetDisconnected("Connection failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunAction(OpsAction? action) =>
        action is not null && !IsBusy && _session is not null && SelectedCount > 0;

    // Opens the confirm dialog; mutates nothing until the user accepts, then submits + polls to terminal.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunAction))]
    private async Task RunAction(OpsAction action, CancellationToken ct)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var targets = Maps.Where(m => m.IsSelected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var request = new ConfirmRequest(
            Title: $"{action.Label} {targets.Count} map(s)?",
            Message: $"Sends {action.Label} to the dual-write gateway for {ConnectionName}.",
            Targets: targets.Select(t => $"{t.Name} · {t.CeEntity} · {t.State}").ToList(),
            ConfirmLabel: action.Label,
            IsDanger: action.Danger,
            Caveat: action.Caveat);

        if (!await _dialogs.ConfirmAsync(request))
        {
            return;
        }

        IsBusy = true;
        Status = $"{action.Label}…";
        try
        {
            var maps = targets.Select(t => t.Map).ToList();
            var response = await session.Gateway.StartActionAsync(action.Type, maps, session.Cid, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_actionTimeout);
            var pollToken = timeoutCts.Token;

            using var timer = new PeriodicTimer(_pollInterval);
            DualWriteRequestStatus? final = null;
            while (await timer.WaitForNextTickAsync(pollToken))
            {
                var status = await session.Gateway.GetStatusAsync(response.RequestId, pollToken);
                if (status.IsTerminal)
                {
                    final = status;
                    break;
                }
            }

            Status = final is { IsSuccess: true }
                ? $"{action.Label} completed."
                : $"{action.Label} failed: {final?.Message ?? final?.State ?? "unknown"}.";

            // Refresh the maps to pick up their new states, preserving the user's selection. A refresh
            // failure must NOT overwrite the action result above (the action still happened), so it's
            // caught separately — the grid just keeps its pre-refresh display.
            try
            {
                var keep = targets.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var refreshed = await session.Gateway.GetMapsAsync(session.Cid, ct);
                PopulateMaps(refreshed, keep);
            }
            catch (Exception)
            {
                Status += " (map list could not refresh)";
            }
        }
        catch (OperationCanceledException)
        {
            Status = ct.IsCancellationRequested ? "Cancelled." : $"{action.Label} timed out.";
        }
        catch (Exception ex)
        {
            Status = $"{action.Label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggleDebug() => !IsBusy && _session is not null && SelectedCount > 0;

    // Enable dual-write debug mode for the selected map(s); scoped to the selection, never all maps.
    [RelayCommand(CanExecute = nameof(CanToggleDebug))]
    private Task EnableDebugForSelected(CancellationToken ct) => SetDebugForSelectedAsync(true, ct);

    // Disable dual-write debug mode for the selected map(s).
    [RelayCommand(CanExecute = nameof(CanToggleDebug))]
    private Task DisableDebugForSelected(CancellationToken ct) => SetDebugForSelectedAsync(false, ct);

    // Toggles IsDebugMode on the F&O DualWriteProjectConfiguration entity for each selected map's
    // project. Debug mode is an F&O-side, project-level flag (verbose dual-write logging to the
    // DualWriteErrorLog table) — so this targets F&O OData, not the gateway. The OData set name is
    // resolved from live $metadata (it varies per environment); every step degrades to a clear,
    // non-sensitive message rather than throwing, and no token is ever surfaced.
    private async Task SetDebugForSelectedAsync(bool enabled, CancellationToken ct)
    {
        if (_session is null)
        {
            DebugStatus = "Connect to the gateway first.";
            return;
        }

        var targets = Maps.Where(m => m.IsSelected).Select(m => m.Map).ToList();
        if (targets.Count == 0)
        {
            DebugStatus = "Select a map first.";
            return;
        }

        var env = _activeEnv();
        if (env is null || string.IsNullOrWhiteSpace(env.Url))
        {
            DebugStatus = "No finance & operations URL is configured for this environment.";
            return;
        }

        var projectIds = targets
            .Select(m => m.ProjectId)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (projectIds.Count == 0)
        {
            DebugStatus = "The selected map(s) have no project id, so debug mode can't be targeted.";
            return;
        }

        IsBusy = true;
        DebugStatus = enabled ? "Enabling debug mode…" : "Disabling debug mode…";
        try
        {
            // Resolve the F&O OData set that exposes IsDebugMode from the environment's live metadata.
            await _metadata.LoadEntitiesAsync(ct).ConfigureAwait(true);
            var set = _metadata.GetEntities()
                .Select(e => e.Name)
                .FirstOrDefault(n => n.Contains(DualWriteDebugMode.EntityLogicalName, StringComparison.OrdinalIgnoreCase));
            if (set is null)
            {
                DebugStatus = $"This environment's OData metadata exposes no '{DualWriteDebugMode.EntityLogicalName}' entity, so debug mode can't be toggled from here.";
                return;
            }

            var body = DualWriteDebugMode.BuildPatchBody(enabled);
            // Full metadata so each record carries an @odata.id we can PATCH directly.
            var getHeaders = new Dictionary<string, string> { ["Accept"] = "application/json;odata.metadata=full" };
            var patchHeaders = new Dictionary<string, string> { ["If-Match"] = "*" };

            var ok = 0;
            var failures = new List<string>();
            foreach (var pid in projectIds)
            {
                var getPath = $"data/{set}?$filter=ProjectId eq '{Uri.EscapeDataString(pid)}'";
                var get = await _odata.SendAsync("GET", getPath, null, getHeaders, ct).ConfigureAwait(true);
                if (!get.IsSuccess)
                {
                    failures.Add($"{pid}: query failed ({get.StatusLine})");
                    continue;
                }

                var record = DualWriteDebugMode.ReadFirstRecord(get.Body);
                if (record is null)
                {
                    failures.Add($"{pid}: no project-config record found");
                    continue;
                }

                var patch = await _odata.SendAsync("PATCH", record.ODataId, body, patchHeaders, ct).ConfigureAwait(true);
                if (patch.IsSuccess)
                {
                    ok++;
                }
                else
                {
                    failures.Add($"{pid}: {patch.StatusLine}");
                }
            }

            var verb = enabled ? "enabled" : "disabled";
            DebugStatus = failures.Count == 0
                ? $"Debug mode {verb} for {ok} project(s)."
                : ok == 0
                    ? $"Debug mode not {verb}: {string.Join("; ", failures)}"
                    : $"Debug mode {verb} for {ok} project(s); {failures.Count} failed: {string.Join("; ", failures)}";
        }
        catch (OperationCanceledException)
        {
            DebugStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            DebugStatus = $"Debug-mode toggle failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Rebuilds the rows, re-subscribing to selection changes and restoring prior selection by id.
    private void PopulateMaps(IReadOnlyList<DualWriteMap> maps, ISet<string>? keepSelectedIds)
    {
        foreach (var old in Maps)
        {
            old.PropertyChanged -= OnRowChanged;
        }

        Maps.Clear();
        foreach (var map in maps)
        {
            var row = MapRowViewModel.From(map);
            if (keepSelectedIds is not null && keepSelectedIds.Contains(row.Id))
            {
                row.IsSelected = true;
            }

            row.PropertyChanged += OnRowChanged;
            Maps.Add(row);
        }

        OnPropertyChanged(nameof(SelectedCount));
        NotifySelectionCommands();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            NotifySelectionCommands();
        }
    }

    private void NotifySelectionCommands()
    {
        RunActionCommand.NotifyCanExecuteChanged();
        EnableDebugForSelectedCommand.NotifyCanExecuteChanged();
        DisableDebugForSelectedCommand.NotifyCanExecuteChanged();
    }

    private void ResetDisconnected(string status)
    {
        Status = status;
        ConnectionName = null;
        PopulateMaps(Array.Empty<DualWriteMap>(), keepSelectedIds: null);
        DisposeSession();
    }

    private void DisposeSession()
    {
        if (_session?.Gateway is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _session = null;
    }

    // The shell discards this VM (without finalization) when the active environment changes; dispose the
    // live gateway session so its owned HttpClient / connection pool isn't leaked for the app's lifetime.
    public void Dispose() => DisposeSession();
}
