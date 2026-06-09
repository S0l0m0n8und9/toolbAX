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
    public void Field_list_stays_height_bounded_so_large_entities_cannot_balloon_it()
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
            var fieldList = view.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(lb => lb.Name == "FieldList");

            Assert.NotNull(fieldList); // CustomersV3 has cached fields, so the list renders
            // A bounded MaxHeight (with the virtualizing ListBox) is what keeps an entity with hundreds
            // of fields from growing the picker unbounded — guard it so the regression can't return.
            Assert.True(double.IsFinite(fieldList!.MaxHeight) && fieldList.MaxHeight > 0,
                "The Query Builder field list must keep a finite MaxHeight (bounded + scrollable).");
        }
        finally
        {
            window.Close();
        }
    }
}
