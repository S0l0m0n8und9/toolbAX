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

/// <summary>Headless render smoke for the Profiles screen: the master list renders and the detail
/// binds the active environment.</summary>
public class ProfilesViewRenderTests
{
    [AvaloniaFact]
    public void Renders_master_list_and_detail_for_the_active_profile()
    {
        var view = new ProfilesView { DataContext = new ProfilesViewModel(new FakeProfileStore()) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ProfilesList");
            Assert.Equal(4, list.ItemCount);

            var title = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "DetailTitle");
            Assert.Equal("USMF Dev", title.Text);   // active = dev-usmf
        }
        finally
        {
            window.Close();
        }
    }
}
