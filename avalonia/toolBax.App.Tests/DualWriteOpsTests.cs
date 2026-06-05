using System;
using System.Linq;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Flagship Operations behaviour (headless-testing.md): state-aware eligibility, the confirm gate
/// (no mutation without confirm), and confirmed actions settling maps + logging the gateway code.
/// Pure VM logic — fast, no view.
/// </summary>
public class DualWriteOpsTests
{
    private static DualWriteOpsViewModel MakeVm(FakeDualWriteGateway gateway, FakeDialogs dialogs) =>
        new(gateway, dialogs, FakeDualWriteGateway.SeedGateway(), FakeDualWriteGateway.SeedMaps(),
            pollInterval: TimeSpan.FromMilliseconds(1));

    [Fact]
    public void Pause_is_eligible_only_for_running_maps()
    {
        var vm = MakeVm(new FakeDualWriteGateway(), new FakeDialogs(confirm: true));
        foreach (var m in vm.Maps) m.IsChecked = true;   // check all
        var pause = vm.Actions.Single(a => a.Id == "pause");

        Assert.Equal(vm.Maps.Count(m => m.State == MapState.Running), vm.EligibleCount(pause));
        Assert.True(vm.CanRun(pause));
    }

    [Fact]
    public void Start_is_eligible_only_for_stopped_or_idle_maps()
    {
        var vm = MakeVm(new FakeDualWriteGateway(), new FakeDialogs(confirm: true));
        foreach (var m in vm.Maps) m.IsChecked = true;
        var start = vm.Actions.Single(a => a.Id == "start");

        Assert.Equal(vm.Maps.Count(m => m.State is MapState.Stopped or MapState.Idle), vm.EligibleCount(start));
    }

    [Fact]
    public async Task RunAction_does_not_call_the_gateway_when_confirm_is_cancelled()
    {
        var gateway = new FakeDualWriteGateway();
        var dialogs = new FakeDialogs(confirm: false);
        var vm = MakeVm(gateway, dialogs);
        vm.Maps.First(m => m.State == MapState.Running).IsChecked = true;

        await vm.RunActionCommand.ExecuteAsync(vm.Actions.Single(a => a.Id == "pause"));

        Assert.Equal(1, dialogs.Calls);     // user was asked
        Assert.Equal(0, gateway.SubmitCount); // but nothing was mutated
    }

    [Fact]
    public async Task Confirmed_pause_settles_running_maps_to_paused_and_logs_the_code()
    {
        var gateway = new FakeDualWriteGateway();
        var vm = MakeVm(gateway, new FakeDialogs(confirm: true));
        var running = vm.Maps.Where(m => m.State == MapState.Running).ToList();
        foreach (var m in running) m.IsChecked = true;

        await vm.ExecuteActionAsync(vm.Actions.Single(a => a.Id == "pause"), running);

        Assert.Equal(1, gateway.SubmitCount);
        Assert.All(running, m => Assert.Equal(MapState.Paused, m.State));
        Assert.False(vm.IsBusy);
        Assert.Contains(vm.Log, l => l.Text.Contains("action=5"));  // pause = code 5
    }

    private sealed class FakeDialogs : IDialogService
    {
        private readonly bool _confirm;
        public FakeDialogs(bool confirm) => _confirm = confirm;
        public int Calls { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Calls++;
            return Task.FromResult(_confirm);
        }
    }
}
