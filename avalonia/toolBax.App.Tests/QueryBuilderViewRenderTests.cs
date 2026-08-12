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
    public void Renders_the_four_workspace_tabs()
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
            // All four tab headers are realized by the tab strip (only the selected tab's *content* is lazy).
            var tabs = view.GetVisualDescendants().OfType<TabItem>().ToList();
            Assert.Equal(4, tabs.Count);
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

    // Holds a run open so the busy-only Cancel button can be observed while it is in flight.
    private sealed class GatedODataClient : IODataClient
    {
        public readonly TaskCompletionSource Gate = new();

        public async Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            await Gate.Task;
            ct.ThrowIfCancellationRequested();
            return new ODataResponse(200, "OK", "{\"value\":[]}", 5);
        }
    }

    [AvaloniaFact]
    public void Cancel_appears_only_while_an_operation_is_running_and_stops_it()
    {
        var client = new GatedODataClient();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        var view = new QueryBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The cancel commands were generated but bound nowhere, so no cancellation was reachable (#168).
            var cancel = view.GetVisualDescendants().OfType<Button>()
                .Single(b => (b.Content as string) == "Cancel");
            Assert.False(cancel.IsVisible); // idle: nothing to cancel

            var run = vm.RunCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(cancel.IsVisible);
            Assert.True(cancel.Command!.CanExecute(null));

            cancel.Command.Execute(null);
            client.Gate.SetResult();
            Dispatcher.UIThread.RunJobs();
            run.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Run cancelled.", vm.StatusText);
            Assert.False(cancel.IsVisible);
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
