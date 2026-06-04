using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FoToolbox.E2eTests.Infrastructure;
using Xunit;

namespace FoToolbox.E2eTests;

public class ProfilesFlowTests
{
    [E2eFact]
    public void Adding_a_profile_and_typing_a_name_updates_the_list()
    {
        AppDriver? driver = null;
        try
        {
            driver = AppDriver.Launch();
            var win = driver.MainWindow;

            var addButton = Retry.WhileNull(
                () => win.FindFirstDescendant(cf => cf.ByAutomationId("ProfilesAddButton")),
                TimeSpan.FromSeconds(10)).Result ?? throw new InvalidOperationException("ProfilesAddButton not found.");
            addButton.AsButton().Invoke();

            const string name = "E2E Test Env";
            var nameBox = Retry.WhileNull(
                () => win.FindFirstDescendant(cf => cf.ByAutomationId("ProfileNameTextBox")),
                TimeSpan.FromSeconds(10)).Result ?? throw new InvalidOperationException("ProfileNameTextBox not found.");

            // Set via the UIA ValuePattern rather than synthesized keyboard/mouse input:
            // the WPF binding (UpdateSourceTrigger=PropertyChanged) fires on this just like a
            // real keystroke, and it is immune to UIPI/focus issues that make Click()/Enter()
            // unreliable when the test host and app run at different integrity levels.
            var tb = nameBox.AsTextBox();
            tb.Text = name;

            // Re-acquire the list (and its items) on every poll so each iteration sees a fresh
            // UIA snapshot — a list/items reference captured once can go stale as WPF re-renders.
            // The item's own Name is the bound VM type; the displayed profile name is rendered in a
            // child TextBlock, so search each item's descendants (and its Name, for robustness).
            var matched = Retry.WhileFalse(
                () =>
                {
                    var list = win.FindFirstDescendant(cf => cf.ByAutomationId("ProfilesList"))?.AsListBox();
                    if (list is null) return false;
                    foreach (var item in list.Items)
                    {
                        if ((item.Name ?? string.Empty).Contains(name, StringComparison.Ordinal)) return true;
                        foreach (var descendant in item.FindAllDescendants())
                        {
                            if ((descendant.Name ?? string.Empty).Contains(name, StringComparison.Ordinal)) return true;
                        }
                    }
                    return false;
                },
                TimeSpan.FromSeconds(10)).Result;
            Assert.True(matched, $"No profile list item reflected the typed name '{name}'.");
        }
        catch
        {
            driver?.CaptureScreenshot(nameof(Adding_a_profile_and_typing_a_name_updates_the_list));
            throw;
        }
        finally
        {
            driver?.Dispose();
        }
    }
}
