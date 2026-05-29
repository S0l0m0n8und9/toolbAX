using System.IO;
using FoToolbox.Core.Profiles;
using Xunit;

namespace FoToolbox.Tests;

public sealed class PluginTrustStoreTests
{
    private static string TempStorePath() =>
        Path.Combine(Directory.CreateTempSubdirectory("trust-store").FullName, "trusted-plugins.json");

    [Fact]
    public void IsTrusted_False_For_Unknown_Plugin()
    {
        var store = new PluginTrustStore(TempStorePath());
        Assert.False(store.IsTrusted("Some.Plugin", "abc123"));
    }

    [Fact]
    public void Add_Then_IsTrusted_RoundTrips_Across_Instances()
    {
        var path = TempStorePath();
        new PluginTrustStore(path).Add("Some.Plugin", "ABC123");

        var reopened = new PluginTrustStore(path);
        Assert.True(reopened.IsTrusted("Some.Plugin", "abc123")); // hash compare is case-insensitive
    }

    [Fact]
    public void IsTrusted_False_When_Hash_Differs()
    {
        var path = TempStorePath();
        var store = new PluginTrustStore(path);
        store.Add("Some.Plugin", "hash-one");

        Assert.False(store.IsTrusted("Some.Plugin", "hash-two"));
    }

    [Fact]
    public void Add_Is_Idempotent()
    {
        var path = TempStorePath();
        var store = new PluginTrustStore(path);
        store.Add("Some.Plugin", "h");
        store.Add("Some.Plugin", "h");

        var json = File.ReadAllText(path);
        // Only one entry serialized.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "Some.Plugin"));
    }
}
