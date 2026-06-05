using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for the flagship Operations view (control-map §3): the grid renders
/// and the command bar's eligibility wiring (CanExecute ← CanRun) tracks map selection.</summary>
public class OpsViewRenderTests
{
    private static DualWriteOpsViewModel MakeVm() => new(
        new FakeDualWriteGateway(),
        new StubDialogs(),
        FakeDualWriteGateway.SeedGateway(),
        FakeDualWriteGateway.SeedMaps(),
        pollInterval: TimeSpan.FromMilliseconds(1));

    private static (Window window, DualWriteOpsView view) Show(DualWriteOpsViewModel vm)
    {
        var view = new DualWriteOpsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static Button Button(DualWriteOpsView view, string content) =>
        view.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content);

    [AvaloniaFact]
    public void Renders_the_maps_grid_and_command_bar()
    {
        var (window, view) = Show(MakeVm());
        try
        {
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());

            var labels = view.GetVisualDescendants().OfType<Button>()
                .Select(b => b.Content as string).ToList();
            foreach (var action in new[] { "Start", "Stop", "Pause", "Resume", "Initial sync" })
            {
                Assert.Contains(action, labels);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Checking_a_running_map_enables_the_stop_button()
    {
        // Proves the command bar wires CanExecute←CanRun: selecting a stop-eligible map enables the
        // button. (The full eligibility matrix, incl. ineligible states, is covered by the VM tests.)
        var vm = MakeVm();
        var (window, view) = Show(vm);
        try
        {
            vm.Maps.First(m => m.State == MapState.Running).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Button(view, "Stop").IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
