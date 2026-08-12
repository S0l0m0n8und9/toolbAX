using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
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

    // --- Header environment switcher (#168) ---

    private static ComboBox Switcher(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EnvSwitcher");

    /// <summary>Counts confirm prompts so a test can prove the switch funnel was (or wasn't) entered.</summary>
    private sealed class CountingDialogs : IDialogService
    {
        private readonly bool _answer;
        public int Calls { get; private set; }
        public CountingDialogs(bool answer = false) => _answer = answer;
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Calls++;
            return Task.FromResult(_answer);
        }
    }

    /// <summary>
    /// A store that can read but never persist the active id (a locked profile.db), counting the attempts
    /// so a test can prove the rolled-back switch was attempted exactly once.
    /// </summary>
    private sealed class CountingUnwritableStore : IProfileStore
    {
        private readonly IReadOnlyList<EnvProfile> _profiles = FakeProfileStore.Seed();
        public int WriteAttempts { get; private set; }
        public IReadOnlyList<EnvProfile> GetAll() => _profiles;
        public void Save(EnvProfile profile) { }
        public void Delete(string id) { }
        public string? ActiveId
        {
            get => _profiles[0].Id;
            set
            {
                WriteAttempts++;
                throw new IOException("profile.db is locked");
            }
        }
    }

    /// <summary>A working store that counts active-id writes, so a test can prove a switch was (or wasn't)
    /// funnelled — a re-fired switch persists the id again even when it lands on the same environment.</summary>
    private sealed class CountingActiveIdStore : IProfileStore
    {
        private readonly FakeProfileStore _inner = new();
        private string? _activeId;
        public int Writes { get; private set; }
        public CountingActiveIdStore() => _activeId = _inner.ActiveId;
        public IReadOnlyList<EnvProfile> GetAll() => _inner.GetAll();
        public void Save(EnvProfile profile) => _inner.Save(profile);
        public void Delete(string id) => _inner.Delete(id);
        public string? ActiveId
        {
            get => _activeId;
            set { Writes++; _activeId = value; }
        }
    }

    [AvaloniaFact]
    public void Header_renders_an_environment_switcher_over_the_shells_environments()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var switcher = Switcher(window);

            // It lives in the header, not the content host or the status strip.
            var header = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HeaderBar");
            Assert.Contains(switcher, header.GetVisualDescendants().OfType<ComboBox>());

            Assert.Equal(shell.Environments.Count, switcher.ItemCount);      // lists every environment…
            Assert.Same(shell.ActiveEnvironment, switcher.SelectedItem);     // …and shows the active one
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Picking_an_environment_in_the_header_switcher_routes_through_the_switch_funnel()
    {
        var store = new FakeProfileStore();
        var dialogs = new CountingDialogs();  // declines the tool refresh
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var switcher = Switcher(window);
            var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

            switcher.SelectedItem = other;   // the user's pick, as the rendered control reports it
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(other.Id, shell.ActiveEnvironment!.Id);  // the shell moved…
            Assert.Equal(other.Id, store.ActiveId);               // …the choice was persisted…
            Assert.Equal(1, dialogs.Calls);                       // …and the refresh prompt was offered once
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_rolled_back_switch_resyncs_the_header_switcher_without_refiring_it()
    {
        // The funnel is transactional: a store that rejects the active-id write reverts ActiveEnvironment.
        // The switcher must follow that revert (it can't sit on an environment the shell isn't using) —
        // and the resulting selection change must NOT be mistaken for a fresh user pick, or a rejected
        // switch would retry itself for as long as the store stays locked.
        var store = new CountingUnwritableStore();
        var dialogs = new CountingDialogs(answer: true);
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var switcher = Switcher(window);
            var previous = shell.ActiveEnvironment!;
            var other = shell.Environments.First(e => e.Id != previous.Id);

            switcher.SelectedItem = other;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(previous, shell.ActiveEnvironment);       // rolled back: the switch didn't happen
            Assert.Same(previous, switcher.SelectedItem);         // …and the box shows the reverted value
            Assert.Equal(1, store.WriteAttempts);                 // fired once — the re-sync didn't re-fire
            Assert.Equal(0, dialogs.Calls);                       // nothing switched, so nothing to refresh
            Assert.Contains("Couldn't switch environment", shell.BackgroundError);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Deleting_the_active_profile_does_not_refire_the_switch_through_the_header_switcher()
    {
        // Deleting the active profile rewrites Environments and picks a replacement WITHOUT going through
        // the deliberate-switch funnel (it refreshes the tools unconditionally and must not prompt). The
        // switcher's selection necessarily moves as the list is reshaped, so that move must not be read as
        // a user pick — otherwise a deletion would raise the prompt it deliberately skips.
        var dialogs = new CountingDialogs();
        var shell = new ShellViewModel(dialogs: dialogs);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var activeId = shell.ActiveEnvironment!.Id;
            shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
            Dispatcher.UIThread.RunJobs();
            var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
            profiles.Selected = profiles.Profiles.Single(p => p.Id == activeId);

            profiles.DeleteProfileCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, dialogs.Calls);                                    // no "refresh open tools?" prompt
            Assert.NotEqual(activeId, shell.ActiveEnvironment!.Id);            // a replacement took over…
            Assert.Same(shell.ActiveEnvironment, Switcher(window).SelectedItem); // …and the box agrees
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Renaming_the_active_profile_resyncs_the_header_switcher_without_refiring_the_switch()
    {
        // A rename replaces the active profile's entry in Environments, which moves the switcher's
        // selection onto the new record before the shell has assigned ActiveEnvironment. That is the list
        // reshaping itself, not a deliberate switch: it must not re-enter the funnel (a rename must
        // neither re-persist the active id nor offer to discard the open tools' unsaved input).
        var store = new CountingActiveIdStore();
        var dialogs = new CountingDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var activeId = shell.ActiveEnvironment!.Id;
            shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
            Dispatcher.UIThread.RunJobs();
            var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
            profiles.Selected = profiles.Profiles.Single(p => p.Id == activeId);
            profiles.DraftName = "Renamed Env";

            profiles.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Renamed Env", shell.ActiveEnvironment!.Name);
            Assert.Same(shell.ActiveEnvironment, Switcher(window).SelectedItem); // box follows the rename
            Assert.Equal(0, store.Writes);   // …without re-persisting: a rename is not a switch
            Assert.Equal(0, dialogs.Calls);  // …and without offering to discard open tool state
        }
        finally
        {
            window.Close();
        }
    }

    // --- Status strip (#168) ---

    [AvaloniaFact]
    public void The_status_strip_carries_no_dead_busy_segment()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var strip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "StatusStrip");
            var segments = strip.GetVisualDescendants().OfType<TextBlock>().ToList();

            // Nothing ever set ShellViewModel.IsBusy, so the segment could only ever read "busy: False".
            Assert.DoesNotContain(segments, t => t.Text?.StartsWith("busy", StringComparison.OrdinalIgnoreCase) == true);

            // …and the segments that do carry live state are still there.
            Assert.Equal("Plugins", segments.Single(t => t.Name == "StatusToolLabel").Text);
            Assert.Equal(shell.ActiveEnvironment!.Name, segments.Single(t => t.Name == "StatusEnvLabel").Text);
        }
        finally
        {
            window.Close();
        }
    }

    // --- Plugins home card grid (#168) ---

    [AvaloniaFact]
    public void Plugins_home_renders_a_card_per_catalog_entry_including_virtual_tables()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(), Width = 1280, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var home = window.GetVisualDescendants().OfType<PluginsHomeView>().Single();
            var cardIds = home.GetVisualDescendants().OfType<Button>()
                .Select(b => b.CommandParameter)
                .OfType<string>()
                .ToList();

            Assert.Equal(new BuiltInToolCatalog().Plugins.Select(p => p.Id).ToList(), cardIds);
            Assert.Contains("virtualtables", cardIds);  // the shipped tool the landing grid used to omit
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
