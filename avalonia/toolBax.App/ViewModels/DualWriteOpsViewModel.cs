using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Flagship Dual-Write Operations screen (control-map §3, viewmodels-and-services §A). Owns the maps
/// grid + gateway log, computes state-aware action eligibility, and enforces confirm-on-mutation:
/// every mutating action opens a confirm dialog before any gateway call, then submits and polls
/// status until the maps settle.
/// </summary>
public partial class DualWriteOpsViewModel : ObservableObject
{
    private readonly IDualWriteGateway _gateway;
    private readonly IDialogService _dialogs;
    private readonly TimeSpan _pollInterval;

    public ObservableCollection<MapRowViewModel> Maps { get; } = new();
    public ObservableCollection<GatewayLogEntry> Log { get; } = new();
    public IReadOnlyList<DwAction> Actions => DwActions.All;
    public GatewayInfo Gateway { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _activeRequestId;
    [ObservableProperty] private int _pollDone;
    [ObservableProperty] private int _pollTotal;

    public DualWriteOpsViewModel(
        IDualWriteGateway gateway,
        IDialogService dialogs,
        GatewayInfo gatewayInfo,
        IEnumerable<DwMap> maps,
        TimeSpan? pollInterval = null)
    {
        _gateway = gateway;
        _dialogs = dialogs;
        Gateway = gatewayInfo;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(600);

        foreach (var map in maps)
        {
            var row = MapRowViewModel.From(map);
            row.PropertyChanged += OnRowChanged;
            Maps.Add(row);
        }
    }

    public int SelectedCount => Maps.Count(m => m.IsChecked);

    /// <summary>Checked maps currently in a state the action applies to.</summary>
    public int EligibleCount(DwAction action) =>
        Maps.Count(m => m.IsChecked && action.AppliesTo.Contains(m.State));

    public bool CanRun(DwAction? action) =>
        action is not null && !IsBusy && EligibleCount(action) > 0;

    // Opens the confirm dialog; mutates nothing until the user accepts.
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAction(DwAction action)
    {
        var targets = Maps.Where(m => m.IsChecked && action.AppliesTo.Contains(m.State)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var request = new ConfirmRequest(
            action,
            Gateway.CName,
            targets.Select(t => new ConfirmTarget(t.FoEntity, t.DvEntity, t.Direction, t.State)).ToList());

        if (!await _dialogs.ConfirmAsync(request))
        {
            return;
        }

        await ExecuteActionAsync(action, targets);
    }

    // The actual gateway call + polling, separated so tests can drive it without the dialog.
    public async Task ExecuteActionAsync(DwAction action, IReadOnlyList<MapRowViewModel> targets)
    {
        IsBusy = true;
        RunActionCommand.NotifyCanExecuteChanged();

        foreach (var map in targets)
        {
            map.State = DwActions.VerbState(action);
        }

        var ids = targets.Select(m => m.TableId).ToList();
        var requestId = await _gateway.SubmitActionAsync(Gateway.Cid, action, ids, CancellationToken.None);
        ActiveRequestId = requestId;
        PollTotal = ids.Count;
        PollDone = 0;
        AppendLog($"POST · action={action.Code} ({action.Id}) · {ids.Count} map(s)", $"requestId {requestId}", LogKind.Info);

        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync())
        {
            var status = await _gateway.GetStatusAsync(requestId, CancellationToken.None);
            ApplyStatus(status);
            if (status.Phase is RequestPhase.Succeeded or RequestPhase.Failed)
            {
                var ok = status.Phase == RequestPhase.Succeeded;
                AppendLog(
                    $"{(ok ? "OK" : "FAILED")} · action={action.Code} · {ids.Count} map(s)",
                    $"requestId {requestId}",
                    ok ? LogKind.Ok : LogKind.Err);
                break;
            }
        }

        IsBusy = false;
        ActiveRequestId = null;
        RunActionCommand.NotifyCanExecuteChanged();
    }

    private void ApplyStatus(GatewayStatus status)
    {
        foreach (var map in Maps)
        {
            if (status.MapStates.TryGetValue(map.TableId, out var state))
            {
                map.State = state;
            }
        }

        PollDone = status.MapStates.Count(kv => !DwActions.IsTransitional(kv.Value));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapRowViewModel.IsChecked) or nameof(MapRowViewModel.State))
        {
            OnPropertyChanged(nameof(SelectedCount));
            RunActionCommand.NotifyCanExecuteChanged();
        }
    }

    private void AppendLog(string text, string? note, LogKind kind) =>
        Log.Add(new GatewayLogEntry(text, note, kind));
}
