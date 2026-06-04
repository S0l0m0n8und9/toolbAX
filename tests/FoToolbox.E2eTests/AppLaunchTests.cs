using System;
using FlaUI.Core.Tools;
using FoToolbox.E2eTests.Infrastructure;
using Xunit;

namespace FoToolbox.E2eTests;

public class AppLaunchTests
{
    [E2eFact]
    public void App_launches_shows_profiles_and_exits_cleanly()
    {
        AppDriver? driver = null;
        try
        {
            driver = AppDriver.Launch();
            Assert.Contains("toolbax", driver.MainWindow.Title, StringComparison.OrdinalIgnoreCase);

            // The "Profiles" entry is always present (default tab when no profile exists).
            var profiles = Retry.WhileNull(
                () => driver!.MainWindow.FindFirstDescendant(cf => cf.ByName("Profiles")),
                TimeSpan.FromSeconds(10)).Result;
            Assert.NotNull(profiles);
        }
        catch
        {
            driver?.CaptureScreenshot(nameof(App_launches_shows_profiles_and_exits_cleanly));
            throw;
        }
        finally
        {
            driver?.Dispose();
        }
    }
}
