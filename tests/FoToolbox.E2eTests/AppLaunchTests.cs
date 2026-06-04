using FoToolbox.E2eTests.Infrastructure;
using Xunit;

namespace FoToolbox.E2eTests;

public class AppLaunchTests
{
    [E2eFact]
    public void App_launches_to_main_window()
    {
        using var driver = AppDriver.Launch();
        Assert.NotNull(driver.MainWindow);
        Assert.Contains("toolBax", driver.MainWindow.Title);
    }
}
