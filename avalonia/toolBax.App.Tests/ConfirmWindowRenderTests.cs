using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>The confirm dialog must visually flag destructive actions (Stop / Initial sync).</summary>
public class ConfirmWindowRenderTests
{
    private static ConfirmWindow Show(string label, bool danger)
    {
        var request = new ConfirmRequest(
            Title: $"{label} 1 map(s)?",
            Message: "Sends the action to the dual-write gateway.",
            Targets: new[] { "Customers V3 · account · Running" },
            ConfirmLabel: label,
            IsDanger: danger,
            Caveat: danger ? "This is destructive." : null);
        var window = new ConfirmWindow { DataContext = new ConfirmDialogViewModel(request) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button ConfirmButton(ConfirmWindow window, string label) =>
        window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == label);

    [AvaloniaFact]
    public void Destructive_action_confirm_button_has_the_danger_class()
    {
        var window = Show("Stop", danger: true);
        try
        {
            Assert.Contains("danger", ConfirmButton(window, "Stop").Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Non_destructive_action_confirm_button_has_no_danger_class()
    {
        var window = Show("Pause", danger: false);
        try
        {
            Assert.DoesNotContain("danger", ConfirmButton(window, "Pause").Classes);
        }
        finally
        {
            window.Close();
        }
    }
}
