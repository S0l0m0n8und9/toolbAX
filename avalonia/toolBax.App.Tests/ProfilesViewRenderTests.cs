using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

            // The profile name is now the inline-editable header title (a TextBox bound to DraftName).
            var title = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "DetailTitle");
            Assert.Equal("USMF Dev", title.Text);   // active = dev-usmf
            Assert.False(title.IsReadOnly);          // editable, not a static label
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Master_list_shows_a_status_dot_per_row_and_marks_only_the_active_profile()
    {
        var view = new ProfilesView { DataContext = new ProfilesViewModel(new FakeProfileStore()) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ProfilesList");

            // One status dot per row.
            Assert.Equal(4, list.GetVisualDescendants().OfType<Ellipse>().Count());

            // Exactly one master-list row shows the "active" badge — the active profile (dev-usmf).
            var activeBadges = list.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text == "active" && t.IsEffectivelyVisible).ToList();
            Assert.Single(activeBadges);
        }
        finally
        {
            window.Close();
        }
    }
}
