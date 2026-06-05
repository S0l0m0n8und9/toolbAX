using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>The confirm dialog must visually flag destructive actions (Stop / Initial sync).</summary>
public class ConfirmWindowRenderTests
{
    private static ConfirmWindow Show(string actionId)
    {
        var action = DwActions.All.Single(a => a.Id == actionId);
        var request = new ConfirmRequest(action, "Contoso (Prod)",
            new[] { new ConfirmTarget("CustomersV3", "account", DwDirection.Both, MapState.Running) });
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
        var window = Show("stop");
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
        var window = Show("pause");
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
