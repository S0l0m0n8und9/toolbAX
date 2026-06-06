using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public class DiAuthModeExtensionsTests
{
    [Fact]
    public void Labels_are_friendly()
    {
        Assert.Equal("Interactive (MFA)", DiAuthMode.Interactive.Label());
        Assert.Equal("ROPC (service account)", DiAuthMode.Ropc.Label());
    }
}
