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
/// The de-risking milestone (headless-testing.md): prove an Avalonia view renders + binds in the
/// headless harness with no display server, and that the design-token resources resolve. Screens are
/// only built once this stays green.
/// </summary>
public class ShellRenderTests
{
    [AvaloniaFact]
    public void MainWindow_renders_and_binds_the_shell_title()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();                 // headless: no real window, but layout/binding run
        Dispatcher.UIThread.RunJobs();

        var title = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "ShellTitle");

        Assert.NotNull(title);
        Assert.Equal("toolBax", title!.Text);
    }

    [AvaloniaFact]
    public void Design_tokens_resolve_from_application_resources()
    {
        // The app-merged Tokens.axaml must supply the brand brushes every screen binds to.
        var found = Application.Current!.Resources.TryGetResource(
            "AccentBrush", ThemeVariant.Dark, out var accent);

        Assert.True(found);
        Assert.NotNull(accent);
    }
}
