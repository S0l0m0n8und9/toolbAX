using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using FoToolbox.E2eTests.Infrastructure;
using Xunit;

namespace FoToolbox.E2eTests;

/// <summary>
/// Layer C follow-on (#41): with a profile seeded into the isolated data dir, the host must load
/// the plugin tabs and let the user switch between them, showing each plugin's tool control.
/// Network-free — the seeded profile has only an F&O environment (no Dataverse, no token), so the
/// plugins load and tabs render without any OData/auth call.
/// </summary>
public class PluginNavigationFlowTests
{
    // The host shows the Profiles tab plus the visible bundled plugins (Hello is hidden), i.e.
    // Profiles + QueryBuilder, TableEntityBrowser, ODataPostBuilder, DualWriteMapBrowser,
    // DualWriteOperations, DualWriteCompare = 7 tabs.
    private const int ExpectedTabCount = 7;

    [E2eFact]
    public void Seeded_profile_loads_plugin_tabs_and_each_is_selectable_and_shows_its_control()
    {
        AppDriver? driver = null;
        try
        {
            driver = AppDriver.Launch(appDataDir =>
            {
                // Seed a single F&O environment (no Dataverse, no token) so a profile is active and
                // the plugins load, without triggering any network/auth on launch.
                var store = new ProfileStore(Path.Combine(appDataDir, "profile.db"));
                store.EnsureCreatedAsync().GetAwaiter().GetResult();
                store.UpsertEnvironmentAsync(new FoEnvironment(
                    "e2e",
                    "E2E Env",
                    "https://e2e.operations.dynamics.com",
                    "00000000-0000-0000-0000-000000000000",
                    "USMF")).GetAwaiter().GetResult();

                // Microsoft.Data.Sqlite pools connections by default, so the seed connection lingers in
                // this (test) process's pool holding an OS handle on profile.db. Clear the pool so the
                // file is fully released before the separate host process opens it (avoids any
                // cross-process "database is locked" contention on launch).
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            });
            var win = driver.MainWindow;

            // Plugin discovery runs asynchronously after the window appears; poll until the tab bar
            // has stabilised at the expected count.
            var tabsLoaded = Retry.WhileFalse(
                () => TabCount(win) == ExpectedTabCount,
                TimeSpan.FromSeconds(60)).Result;
            if (!tabsLoaded)
            {
                // Report every top-level window: a blocking modal (e.g. an unsigned-plugin trust
                // prompt) leaves the main window up but empty, which is the failure mode to surface.
                var windows = driver.App.GetAllTopLevelWindows(driver.Automation);
                var titles = string.Join(" | ", System.Linq.Enumerable.Select(windows, w => $"'{w.Title}'"));
                Assert.Fail(
                    $"Tab bar did not reach {ExpectedTabCount} tabs (last {TabCount(win)}, navRail {NavRailCount(win)}). " +
                    $"Top-level windows: [{titles}].");
            }

            // The left nav rail mirrors the same entries.
            var navMirrors = Retry.WhileFalse(
                () => NavRailCount(win) == ExpectedTabCount,
                TimeSpan.FromSeconds(10)).Result;
            Assert.True(navMirrors, $"FoNavRail did not mirror the tab count (last {NavRailCount(win)} vs {ExpectedTabCount}).");

            // Every tab must be selectable and switch the content host to a (non-empty) control.
            for (var i = 0; i < ExpectedTabCount; i++)
            {
                var index = i;

                var selected = Retry.WhileFalse(
                    () =>
                    {
                        var items = win.FindFirstDescendant(cf => cf.ByAutomationId("FoTabBar"))?.AsListBox()?.Items;
                        if (items is null || items.Length <= index) return false;
                        items[index].Select();
                        return items[index].IsSelected;
                    },
                    TimeSpan.FromSeconds(10)).Result;
                Assert.True(selected, $"Tab at index {index} could not be selected.");

                // The content host is a bare ContentControl, which WPF does not surface in the UIA
                // control tree (only its hosted control is promoted), so anchor on screen region: the
                // active plugin's control must render something in the content area — right of the nav
                // rail, below the tab bar, above the status-bar strip.
                var shown = Retry.WhileFalse(
                    () => ContentAreaHasControl(win),
                    TimeSpan.FromSeconds(10)).Result;
                Assert.True(shown, $"Selecting tab {index} did not render a control in the content area.");
            }
        }
        catch
        {
            driver?.CaptureScreenshot(nameof(Seeded_profile_loads_plugin_tabs_and_each_is_selectable_and_shows_its_control));
            throw;
        }
        finally
        {
            driver?.Dispose();
        }
    }

    private static int TabCount(Window win) =>
        win.FindFirstDescendant(cf => cf.ByAutomationId("FoTabBar"))?.AsListBox()?.Items.Length ?? 0;

    private static int NavRailCount(Window win) =>
        win.FindFirstDescendant(cf => cf.ByAutomationId("FoNavRail"))?.AsListBox()?.Items.Length ?? 0;

    /// <summary>
    /// True when at least one control is rendered in the plugin content area: right of the nav rail,
    /// below the tab bar and above the bottom status-bar strip. Coordinates are all read from the live
    /// tree so the comparison is relative (DPI-safe).
    /// </summary>
    private static bool ContentAreaHasControl(Window win)
    {
        var navRail = win.FindFirstDescendant(cf => cf.ByAutomationId("FoNavRail"));
        var tabBar = win.FindFirstDescendant(cf => cf.ByAutomationId("FoTabBar"));
        if (navRail is null || tabBar is null) return false;

        var left = navRail.BoundingRectangle.Right;
        var top = tabBar.BoundingRectangle.Bottom;
        var bottom = win.BoundingRectangle.Bottom - 60; // exclude the status-bar strip

        foreach (var d in win.FindAllDescendants())
        {
            try
            {
                var r = d.BoundingRectangle;
                if (r.Width <= 0 || r.Height <= 0) continue;
                var cx = r.Left + (r.Width / 2);
                var cy = r.Top + (r.Height / 2);
                if (cx >= left && cy >= top && cy <= bottom) return true;
            }
            catch { /* element went stale between enumeration and read */ }
        }
        return false;
    }
}
