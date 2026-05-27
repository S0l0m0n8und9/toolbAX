using System;
using System.Collections.Generic;
using System.Windows.Media;
using FoToolbox.Host.Plugins;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.Tests;

public class IconResourceResolverTests
{
    private static FoPluginManifest Manifest(string name, string? icon = null) => new()
    {
        Id = "test." + name.ToLowerInvariant(),
        Name = name,
        Version = "0.0.1",
        MinSdk = "0.2.0",
        Icon = icon,
    };

    private static Geometry FromPath(string path) => Geometry.Parse(path);

    [Fact]
    public void Resolve_ExplicitIcon_FindsResourceByKey()
    {
        var profiles = FromPath("M0 0 L 1 1");
        Geometry? Lookup(string key) => key == "Icon.Profiles" ? profiles : null;

        var result = IconResourceResolver.Resolve(Manifest("Anything", icon: "Profiles"), Lookup);

        Assert.Same(profiles, result);
    }

    [Fact]
    public void Resolve_NameHeuristic_FallsBackWhenIconKeyAbsent()
    {
        var query = FromPath("M2 2 L 3 3");
        Geometry? Lookup(string key) => key == "Icon.Query" ? query : null;

        var result = IconResourceResolver.Resolve(Manifest("Query Builder"), Lookup);

        Assert.Same(query, result);
    }

    [Fact]
    public void Resolve_UnknownExplicitIcon_FallsThroughToNameHeuristicThenDefault()
    {
        var plugin = FromPath("M9 9 L 1 1");
        Geometry? Lookup(string key) => key == "Icon.Plugin" ? plugin : null;

        var result = IconResourceResolver.Resolve(Manifest("WeirdName", icon: "NotAKnownIcon"), Lookup);

        Assert.Same(plugin, result);
    }

    [Fact]
    public void Resolve_AllLookupsMiss_ReturnsNull()
    {
        Geometry? Lookup(string key) => null;
        var result = IconResourceResolver.Resolve(Manifest("Whatever"), Lookup);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Profiles", "Icon.Profiles")]
    [InlineData("Query Builder", "Icon.Query")]
    [InlineData("DualWrite Map Browser", "Icon.DualWrite")]
    [InlineData("Table Entity Browser", "Icon.TableEntity")]
    [InlineData("Some Metadata Tool", "Icon.TableEntity")]
    [InlineData("OData POST Builder", "Icon.ODataPost")]
    public void Resolve_NameHeuristic_PicksExpectedKey(string name, string expectedKey)
    {
        var marker = FromPath("M5 5 L 5 6");
        Geometry? Lookup(string key) => key == expectedKey ? marker : null;

        var result = IconResourceResolver.Resolve(Manifest(name), Lookup);

        Assert.Same(marker, result);
    }
}
