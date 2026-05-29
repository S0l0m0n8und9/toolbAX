using System;
using FoToolbox.Host.Plugins;
using Xunit;

namespace FoToolbox.Tests;

[Collection("EnvVars")]
public sealed class PluginTrustOptionsTests
{
    [Fact]
    public void Default_DoesNotAllowUnsigned()
    {
        Assert.False(PluginTrustOptions.Default.AllowUnsigned);
    }

    [Fact]
    public void FromEnvironment_AllowUnsigned_False_When_Unset()
    {
        Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", null);
        Assert.False(PluginTrustOptions.FromEnvironment().AllowUnsigned);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("1", false)]
    public void FromEnvironment_AllowUnsigned_Only_True_When_Literal_True(string value, bool expected)
    {
        try
        {
            Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", value);
            Assert.Equal(expected, PluginTrustOptions.FromEnvironment().AllowUnsigned);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", null);
        }
    }
}
