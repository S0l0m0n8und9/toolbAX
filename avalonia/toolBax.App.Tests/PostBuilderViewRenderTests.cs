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

/// <summary>Headless render smoke for the POST Builder (control-map §7).</summary>
public class PostBuilderViewRenderTests
{
    [AvaloniaFact]
    public void Renders_method_combo_path_and_send_button()
    {
        var view = new PostBuilderView { DataContext = new PostBuilderViewModel(new FakeODataClient()) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(view.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault());
            var send = view.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Send");
            Assert.True(send.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
