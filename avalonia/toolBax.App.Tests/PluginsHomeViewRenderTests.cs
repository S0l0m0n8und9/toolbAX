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

/// <summary>Headless render smoke for the Plugins home (control-map §1).</summary>
public class PluginsHomeViewRenderTests
{
    [AvaloniaFact]
    public void Renders_a_card_per_plugin_and_a_filter_box()
    {
        var vm = new PluginsHomeViewModel(new FakePluginCatalog(), "USMF Dev", openTool: null);
        var view = new PluginsHomeView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(view.GetVisualDescendants().OfType<TextBox>().FirstOrDefault());
            // One clickable card button per plugin.
            var cards = view.GetVisualDescendants().OfType<Button>().Count();
            Assert.True(cards >= vm.Plugins.Count);
        }
        finally
        {
            window.Close();
        }
    }
}
