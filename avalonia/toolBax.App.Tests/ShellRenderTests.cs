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
            var contentTitle = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "ContentTitle");
            Assert.NotNull(contentTitle);
            Assert.Equal("Plugins", contentTitle!.Text);   // default tool is the Plugins home

            var navRail = window.GetVisualDescendants().OfType<ListBox>().First();
            Assert.Equal(8, navRail.ItemCount);
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
