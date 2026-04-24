using DualWriteMapBrowserPlugin;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserTestifyResultTests
{
    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsTrue_ForCreateOnlyRun()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 0,
            patchesPlanned: 0);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsTrue_WhenAllPatchesCompleted()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 3,
            patchesPlanned: 3);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsFalse_WhenPatchSequenceStopsEarly()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 1,
            patchesPlanned: 2);

        Assert.False(succeeded);
    }
}
