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
public partial class DualWriteOpsViewModel : ObservableObject
{
    private readonly IDualWriteConnector _connector;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly IDialogService _dialogs;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _actionTimeout;
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

    // Named actions so each command-bar button binds RunActionCommand with its own parameter.
    public OpsAction StartAction => Actions[0];
    public OpsAction StopAction => Actions[1];
    public OpsAction PauseAction => Actions[2];
    public OpsAction ResumeAction => Actions[3];
    public OpsAction InitialAction => Actions[4];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    private string? _connectionName;

    [ObservableProperty]
    private string _status = "Not connected.";

    [ObservableProperty]
    private string? _loadError;

    public bool IsConnected => ConnectionName is not null;

    public bool HasMaps => Maps.Count > 0;

    public int SelectedCount => Maps.Count(m => m.IsSelected);

    public DualWriteOpsViewModel(
        IDualWriteConnector connector,
        Func<EnvProfile?> activeEnv,
        IDialogService dialogs,
        TimeSpan? pollInterval = null,
        TimeSpan? actionTimeout = null)
    {
        _connector = connector;
        _activeEnv = activeEnv;
        _dialogs = dialogs;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(600);
        // Safety net: never poll a stuck request forever (a hung gateway worker would otherwise lock the UI).
        _actionTimeout = actionTimeout ?? TimeSpan.FromSeconds(120);
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

            // Refresh the maps to pick up their new states, preserving the user's selection.
            var keep = targets.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var refreshed = await session.Gateway.GetMapsAsync(session.Cid, ct);
            PopulateMaps(refreshed, keep);
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
        RunActionCommand.NotifyCanExecuteChanged();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            RunActionCommand.NotifyCanExecuteChanged();
        }
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
}
