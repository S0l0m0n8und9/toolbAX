using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for the redesigned Dual-Write Map Browser (control-map §4).</summary>
public class DualWriteMapViewRenderTests
{
    private sealed class WarningReader : IDualWriteMapReader
    {
        private static readonly DwMapRecord Incomplete = DualWriteMapParser.ParsePage(
            "{\"value\":[{\"msdyn_name\":\"Map\",\"msdyn_mapping\":\"{bad\"}]}").Records.Single();
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) =>
            Task.FromResult(DwSolutionLoadResult.Fail("solution warning"));
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(new[] { Incomplete }));
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) =>
            Task.FromResult(DwCountResult.Ok(0));
    }

    [AvaloniaFact]
    public async Task Solution_and_incomplete_warnings_stack_above_visible_detail_content()
    {
        var vm = new DualWriteMapViewModel(new WarningReader());
        var view = new DualWriteMapView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        await vm.InitializeCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            var solution = Assert.IsType<Border>(view.FindControl<Border>("SolutionWarningBanner"));
            var incomplete = Assert.IsType<Border>(view.FindControl<Border>("IncompleteDetailsBanner"));
            var detail = Assert.IsType<Grid>(view.FindControl<Grid>("DetailContent"));
            Assert.True(solution.IsEffectivelyVisible);
            Assert.True(incomplete.IsEffectivelyVisible);
            Assert.True(detail.IsEffectivelyVisible);
            Assert.Contains(solution, window.GetVisualDescendants());
            Assert.True(solution.Bounds.Width > 0 && solution.Bounds.Height > 0);
            Assert.True(incomplete.Bounds.Width > 0 && incomplete.Bounds.Height > 0);
            Assert.True(detail.Bounds.Width > 0 && detail.Bounds.Height > 0);
            Assert.True(solution.Bounds.Bottom <= incomplete.Bounds.Top);
            Assert.True(incomplete.Bounds.Bottom <= detail.Bounds.Top);
        }
        finally { window.Close(); }
    }
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
    public void Renders_the_solution_and_publisher_filter_dropdowns()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader());
        var (view, window) = Show(vm);
        try
        {
            var combos = view.GetVisualDescendants().OfType<ComboBox>().ToList();
            Assert.True(combos.Count >= 2); // publisher + solution
            // Initialize ran on Loaded, so the solution picker is populated (All sentinel + seeded).
            Assert.Contains(vm.Solutions, s => s.IsAll);
            Assert.Contains(vm.Solutions, s => s.UniqueName == "dualwrite_core");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Row_counts_tab_binds_the_count_rows()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader());
        var (view, window) = Show(vm);
        try
        {
            var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedItem = tabs.GetVisualDescendants().OfType<TabItem>().First(t => (t.Header as string) == "Row counts");
            Dispatcher.UIThread.RunJobs();

            var countsGrid = view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "CountsGrid");
            Assert.Same(vm.CountRows, countsGrid.ItemsSource);
            Assert.NotEmpty(vm.CountRows); // a row per leg of the default-selected map
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
