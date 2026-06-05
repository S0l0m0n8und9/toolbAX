using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for the Dual-Write Map Browser (control-map §4).</summary>
public class DualWriteMapViewRenderTests
{
    private static (DualWriteMapView view, Window window) Show(DualWriteMapViewModel vm)
    {
        var view = new DualWriteMapView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, window);
    }

    [AvaloniaFact]
    public void Renders_master_list_and_bindings_grid()
    {
        var (view, window) = Show(new DualWriteMapViewModel(new FakeDualWriteMapService()));
        try
        {
            Assert.NotNull(view.GetVisualDescendants().OfType<ListBox>().FirstOrDefault());
            Assert.NotNull(view.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Sparkline_path_has_geometry_for_a_selected_map()
    {
        var (view, window) = Show(new DualWriteMapViewModel(new FakeDualWriteMapService()));
        try
        {
            var spark = view.GetVisualDescendants().OfType<Path>().First(p => p.Name == "SparkPath");
            Assert.NotNull(spark.Data);
        }
        finally
        {
            window.Close();
        }
    }
}
