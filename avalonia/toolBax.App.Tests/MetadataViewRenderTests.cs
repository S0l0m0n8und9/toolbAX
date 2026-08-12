using System.Collections.Generic;
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

    // Holds the field fetch open so the in-flight pane can be inspected.
    private sealed class BlockingFieldsMetadata : IMetadataService
    {
        private static readonly EntitySet[] All = { new("Alpha", "M", 1, "k", false, "odata") };
        private static readonly EntityField[] Props = { new("Id", "String", false, IsKey: true, Length: 10) };
        private bool _loaded;

        public TaskCompletionSource<bool> Gate { get; } = new();

        public IReadOnlyList<EntitySet> GetEntities() => All;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => _loaded ? Props : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            await Gate.Task;
            _loaded = true;
            return true;
        }
    }

    [AvaloniaFact]
    public void Shows_a_loading_indicator_in_the_detail_pane_while_the_fields_are_fetched()
    {
        // The view kicks the $metadata fetch off on Loaded, so showing the window with the fetch gated is
        // exactly the state a user sees after clicking through to an entity whose fields aren't cached.
        var service = new BlockingFieldsMetadata();
        var view = new MetadataView { DataContext = new MetadataViewModel(service) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var loading = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "FieldsLoadingHint");
            var notCached = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NotCachedHint");

            Assert.True(loading.IsVisible);
            Assert.False(notCached.IsVisible);   // the pane says "working", not "go look elsewhere"
            Assert.Contains(loading.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Loading Alpha…");

            service.Gate.SetResult(true);
            Dispatcher.UIThread.RunJobs();

            Assert.False(loading.IsVisible);
            Assert.False(notCached.IsVisible);   // the property grid took over
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
