using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Headless render smoke for the shell (control-map §0): the view instantiates, binds the current
/// tool into the content host, renders the nav rail, and the design tokens resolve — all with no
/// display server. View-binding breaks the pure-VM tests can't catch surface here.
/// </summary>
public class ShellRenderTests
{
    [AvaloniaFact]
    public void Shell_renders_and_binds_current_tool_and_nav_rail()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The default tool routes the Plugins home into the content host.
            Assert.NotNull(window.GetVisualDescendants().OfType<PluginsHomeView>().FirstOrDefault());

            var statusToolLabel = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "StatusToolLabel");
            Assert.NotNull(statusToolLabel);
            Assert.Equal("Plugins", statusToolLabel!.Text);   // default tool is the Plugins home

            var navRail = window.GetVisualDescendants()
                .OfType<ListBox>()
                .First(lb => lb.Name == "NavRail");
            Assert.Equal(9, navRail.ItemCount); // + Virtual Tables (#23)
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Opening_a_plugin_card_navigates_the_shell()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = window.GetVisualDescendants().OfType<PluginsHomeView>().First()
                .GetVisualDescendants().OfType<Button>()
                .First(b => (b.CommandParameter as string) == "query");

            Assert.NotNull(card.Command); // the $parent-scoped command binding resolved
            card.Command!.Execute(card.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("query", shell.CurrentTool.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Degraded_mode_renders_a_persistent_banner()
    {
        var shell = new ShellViewModel(degraded: new DegradedMode("profile store unavailable: database is locked"));
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var banner = window.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Name == "DegradedBanner");
            Assert.NotNull(banner);
            Assert.True(banner!.IsVisible);

            var text = banner.GetVisualDescendants().OfType<TextBlock>().First().Text;
            Assert.Contains("Offline sample data", text);
            Assert.Contains("Nothing on screen is live", text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_healthy_shell_renders_no_degraded_banner()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var banner = window.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Name == "DegradedBanner");
            Assert.NotNull(banner);            // present in the tree…
            Assert.False(banner!.IsVisible);   // …but collapsed when nothing is degraded
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Design_tokens_resolve_from_application_resources()
    {
        var found = Application.Current!.Resources.TryGetResource(
            "AccentBrush", ThemeVariant.Dark, out var accent);

        Assert.True(found);
        Assert.NotNull(accent);
    }
}
