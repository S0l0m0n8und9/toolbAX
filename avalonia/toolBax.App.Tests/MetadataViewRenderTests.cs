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

/// <summary>Headless render smoke for the Metadata Browser (control-map §6).</summary>
public class MetadataViewRenderTests
{
    [AvaloniaFact]
    public void Renders_entity_list_and_property_grid_for_a_cached_entity()
    {
        // Default selection (CustomersV3) is cached → the property grid is shown.
        var service = new FakeMetadataService();
        var view = new MetadataView { DataContext = new MetadataViewModel(service) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EntityList");
            Assert.Equal(service.GetEntities().Count, list.ItemCount);

            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.True(grid.IsVisible);
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());
        }
        finally
        {
            window.Close();
        }
    }
}
