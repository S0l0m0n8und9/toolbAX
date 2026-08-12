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

    [AvaloniaFact]
    public void The_include_column_header_explains_what_a_blank_included_field_means_on_patch()
    {
        // On a PATCH the checkbox carries the omit/clear distinction (#158), which isn't guessable from a
        // column called "Incl" — so the tip has to actually reach the rendered header, not just the markup.
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new FakeMetadataService())
        {
            Method = "PATCH",
            UseFieldGrid = true,
        };
        var view = new PostBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var header = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == "Incl");

            Assert.Contains("clears the field", (string)ToolTip.GetTip(header)!);
        }
        finally
        {
            window.Close();
        }
    }
}
