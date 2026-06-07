using ToolBax.App.ViewModels;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class ConfirmDialogViewModelTests
{
    [Fact]
    public void Projects_the_request_into_title_message_and_targets()
    {
        var request = new ConfirmRequest(
            Title: "Pause 1 map(s)?",
            Message: "Sends Pause to the dual-write gateway for Contoso (Prod).",
            Targets: new[] { "Customers V3 · account · Running" },
            ConfirmLabel: "Pause",
            IsDanger: false);

        var vm = new ConfirmDialogViewModel(request);

        Assert.Equal("Pause 1 map(s)?", vm.Title);
        Assert.Contains("Pause", vm.Message);
        Assert.Single(vm.Targets);
        Assert.Equal("Pause", vm.ConfirmLabel);
        Assert.False(vm.HasCaveat);
        Assert.False(vm.IsDanger);
    }

    [Fact]
    public void A_danger_request_with_a_caveat_is_flagged()
    {
        var request = new ConfirmRequest(
            Title: "Stop 1 map(s)?",
            Message: "Sends Stop to the dual-write gateway.",
            Targets: new[] { "Customers V3 · account · Running" },
            ConfirmLabel: "Stop",
            IsDanger: true,
            Caveat: "This halts replication for the selected maps.");

        var vm = new ConfirmDialogViewModel(request);

        Assert.True(vm.IsDanger);
        Assert.True(vm.HasCaveat);
        Assert.Equal("This halts replication for the selected maps.", vm.Caveat);
    }
}
