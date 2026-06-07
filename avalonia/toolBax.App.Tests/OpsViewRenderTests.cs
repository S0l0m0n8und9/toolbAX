using System.Linq;
using System.Threading.Tasks;
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

/// <summary>Headless render smoke for the Operations view (control-map §3): once connected, the maps
/// grid renders the real maps; the Connect affordance is present.</summary>
public class OpsViewRenderTests
{
    private static EnvProfile Env() =>
        new("env1", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected);

    private static (Window window, DualWriteOpsView view) Show(DualWriteOpsViewModel vm)
    {
        var view = new DualWriteOpsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public async Task Renders_the_maps_grid_after_connecting()
    {
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env);
        await vm.LoadCommand.ExecuteAsync(null);

        var (window, view) = Show(vm);
        try
        {
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());

            var buttons = view.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
            Assert.Contains("Connect", buttons);
        }
        finally
        {
            window.Close();
        }
    }
}
