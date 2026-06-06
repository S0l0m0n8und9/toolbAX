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

/// <summary>Headless render smoke for the redesigned Dual-Write Map Browser (control-map §4).</summary>
public class DualWriteMapViewRenderTests
{
    private static (DualWriteMapView view, Window window) Show(DualWriteMapViewModel vm)
    {
        var view = new DualWriteMapView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs(); // fires Loaded → InitializeCommand (loads the fake catalogue)
        return (view, window);
    }

    [AvaloniaFact]
    public void Loads_the_master_list_and_detail_grids_on_show()
    {
        var (view, window) = Show(new DualWriteMapViewModel(new FakeDualWriteMapReader()));
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();
            Assert.NotNull(list);
            // Initialize ran on Loaded, so the master list is populated and a map is selected.
            Assert.NotEmpty(((DualWriteMapViewModel)view.DataContext!).Maps);
            Assert.NotNull(view.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Field_mappings_tab_binds_the_selected_maps_fields()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader());
        var (view, window) = Show(vm);
        try
        {
            var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = 1; // "Field mappings"
            Dispatcher.UIThread.RunJobs();

            var fieldsGrid = view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "FieldsGrid");
            Assert.Same(vm.DetailMap!.Fields, fieldsGrid.ItemsSource);
            Assert.NotEmpty(vm.DetailMap.Fields);
        }
        finally
        {
            window.Close();
        }
    }
}
