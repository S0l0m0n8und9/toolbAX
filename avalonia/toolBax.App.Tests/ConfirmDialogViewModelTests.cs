using System.Linq;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class ConfirmDialogViewModelTests
{
    private static ConfirmRequest Request(string actionId, params (string fo, string dv, DwDirection dir, MapState st)[] targets)
    {
        var action = DwActions.All.Single(a => a.Id == actionId);
        return new ConfirmRequest(
            action,
            "Contoso (Prod)",
            targets.Select(t => new ConfirmTarget(t.fo, t.dv, t.dir, t.st)).ToList());
    }

    [Fact]
    public void Maps_action_and_targets_into_title_and_body()
    {
        var vm = new ConfirmDialogViewModel(Request("pause",
            ("CustomersV3", "account", DwDirection.Both, MapState.Running)));

        Assert.Equal("Pause 1 map(s)?", vm.Title);
        Assert.Contains("action=5", vm.Message);     // pause = code 5
        Assert.Single(vm.Targets);
        Assert.Equal("Pause", vm.ConfirmLabel);
        Assert.False(vm.HasCaveat);                   // pause is not destructive
        Assert.False(vm.IsDanger);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("initial")]
    public void Destructive_actions_show_a_danger_caveat(string actionId)
    {
        var vm = new ConfirmDialogViewModel(Request(actionId,
            ("F", "d", DwDirection.Both, MapState.Running)));

        Assert.True(vm.IsDanger);
        Assert.True(vm.HasCaveat);
        Assert.NotNull(vm.Caveat);
    }
}
