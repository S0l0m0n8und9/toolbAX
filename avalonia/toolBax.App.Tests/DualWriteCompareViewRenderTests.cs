using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for Dual-Write Compare (control-map §5).</summary>
public class DualWriteCompareViewRenderTests
{
    [AvaloniaFact]
    public void Renders_two_pickers_and_a_compare_button()
    {
        var view = new DualWriteCompareView
        {
            DataContext = new DualWriteCompareViewModel(new FakeProfileStore(), new FakeDualWriteCompareService()),
        };
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(view.GetVisualDescendants().OfType<ComboBox>().Count() >= 2);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Compare");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Comparing_populates_the_diff_grid()
    {
        var vm = new DualWriteCompareViewModel(new FakeProfileStore(), new FakeDualWriteCompareService());
        var view = new DualWriteCompareView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.CompareCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var grid = view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "DiffGrid");
            Assert.Same(vm.DiffRows, grid.ItemsSource);
            Assert.NotEmpty(vm.DiffRows);
        }
        finally
        {
            window.Close();
        }
    }
}
