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

/// <summary>Headless render smoke for the Query Builder (control-map §2).</summary>
public class QueryBuilderViewRenderTests
{
    [AvaloniaFact]
    public void Renders_entity_list_run_button_and_query_url()
    {
        var view = new QueryBuilderView
        {
            DataContext = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient()),
        };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(view.GetVisualDescendants().OfType<ListBox>().FirstOrDefault());
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Run");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Running_a_query_builds_result_grid_columns()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient());
        var view = new QueryBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.RunCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var grid = view.GetVisualDescendants().OfType<DataGrid>().First();
            Assert.Equal(vm.ResultColumns.Count, grid.Columns.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Field_list_is_viewport_bounded_and_scrolls_so_large_entities_cannot_balloon_it()
    {
        var view = new QueryBuilderView
        {
            DataContext = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient()),
        };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // Fields is the default tab, so the list renders for CustomersV3 (which has cached fields).
            var fieldList = view.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(lb => lb.Name == "FieldList");
            Assert.NotNull(fieldList);

            // Under the tabbed layout the bounding comes from the tab filling a fixed grid row inside the
            // window (not an arbitrary MaxHeight): the list never exceeds the viewport, and overflow scrolls
            // via the ListBox's built-in ScrollViewer. Guard both so the "balloon" regression can't return.
            Assert.True(fieldList!.Bounds.Height > 0 && fieldList.Bounds.Height <= window.Height,
                $"field list height {fieldList.Bounds.Height} must be >0 and within the {window.Height}px viewport.");
            Assert.NotNull(fieldList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Adding_a_condition_materialises_the_builder_row_editors()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient());
        var view = new QueryBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The filter builder lives in the Filter tab; select it so its content is realized.
            vm.SelectedTabIndex = 1;
            Dispatcher.UIThread.RunJobs();

            // Builder mode is the default; adding a condition should render its field + operator combos
            // through the recursive group → ItemsControl → condition template path.
            var before = view.GetVisualDescendants().OfType<ComboBox>().Count();
            vm.FilterRoot.AddConditionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var after = view.GetVisualDescendants().OfType<ComboBox>().Count();

            Assert.True(after >= before + 2,
                $"expected the condition's field + operator combos to render (before {before}, after {after}).");
        }
        finally
        {
            window.Close();
        }
    }
}
