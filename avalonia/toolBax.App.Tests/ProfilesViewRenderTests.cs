using System;
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

            // One status dot per row (filter by name so Fluent's own template Ellipses can't inflate it).
            Assert.Equal(4, list.GetVisualDescendants().OfType<Ellipse>().Count(e => e.Name == "StatusDot"));

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

    [AvaloniaFact]
    public void Sign_out_tooltip_states_its_real_tenant_wide_scope()
    {
        // #168 low: the tooltip promised per-profile scope while the action deletes the MSAL cache blob
        // for clientId|tenantId — signing out every profile on the shared app registration. It sat right
        // next to the status message that now says so, contradicting it. Pin the wording so the button's
        // promise and ProfilesViewModel.SignOut's status cannot drift apart again.
        var view = new ProfilesView { DataContext = new ProfilesViewModel(new FakeProfileStore()) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var signOut = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == "Sign out");
            var tip = ToolTip.GetTip(signOut) as string;

            Assert.NotNull(tip);
            Assert.Contains("every profile", tip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("for this profile", tip, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Close();
        }
    }
}
