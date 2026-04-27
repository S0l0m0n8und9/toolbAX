using FoToolbox.Core.Auth;
using FoToolbox.Host;
using Xunit;

namespace FoToolbox.Tests;

public class AuthReauthCoordinatorTests
{
    [Trait("Category", "Auth")]
    [Fact]
    public void Notify_Raises_ReauthRequired_With_Original_Exception()
    {
        var coordinator = new AuthReauthCoordinator();
        var recovery = new AuthRecoveryException(
            "Finance and Operations",
            "Finance and Operations authentication needs to be refreshed. Re-authenticate from Profiles and apply the profile again.");

        AuthRecoveryException? notified = null;
        coordinator.ReauthRequired += exception => notified = exception;

        coordinator.Notify(recovery);

        Assert.Same(recovery, notified);
        Assert.Equal("Finance and Operations sign-in required", notified?.PromptTitle);
        Assert.True(notified?.RequiresInteractiveReauth);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void Notify_Suppresses_Duplicate_Prompts_Until_Reset()
    {
        var coordinator = new AuthReauthCoordinator();
        var recovery = new AuthRecoveryException(
            "Dataverse",
            "Dataverse needs you to sign in again. Open Profiles, re-authenticate for this environment, save the updated credentials, and apply the profile again.");

        var notifications = 0;
        coordinator.ReauthRequired += _ => notifications++;

        coordinator.Notify(recovery);
        coordinator.Notify(recovery);
        coordinator.Reset();
        coordinator.Notify(recovery);

        Assert.Equal(2, notifications);
    }
}
