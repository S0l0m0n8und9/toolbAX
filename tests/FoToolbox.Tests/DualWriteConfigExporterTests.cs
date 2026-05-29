using System;
using System.Collections.Generic;
using System.Text.Json;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteConfigExporterTests
{
    private static DualWriteMap Map(string name, string version) =>
        new($"id-{name}", name, name, $"pid-{name}", "Running",
            new DualWriteTemplate($"t-{version}", version, "MS"),
            new[] { new DualWriteTemplate($"t-{version}", version, "MS") });

    [Trait("Category", "DualWrite")]
    [Fact]
    public void ExportJson_IncludesEnvironmentAndMapsSnapshot()
    {
        var env = new DualWriteEnvironment("C1", "contoso", "uat-fo");
        var maps = new List<DualWriteMap> { Map("Customers", "1.0"), Map("Vendors", "2.1") };
        var ts = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

        var json = DualWriteConfigExporter.ExportJson(env, maps, ts);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2026-05-29T12:00:00.0000000+00:00", root.GetProperty("exportedUtc").GetString());
        Assert.Equal("uat-fo", root.GetProperty("environment").GetProperty("identifier").GetString());
        Assert.Equal("C1", root.GetProperty("environment").GetProperty("cid").GetString());
        Assert.Equal(2, root.GetProperty("mapCount").GetInt32());

        var first = root.GetProperty("maps")[0];
        Assert.Equal("Customers", first.GetProperty("name").GetString());
        Assert.Equal("1.0", first.GetProperty("activeVersion").GetString());
        Assert.Equal("pid-Customers", first.GetProperty("projectId").GetString());
        Assert.Single(first.GetProperty("templates").EnumerateArray());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void ExportJson_SortsMapsByDisplayName()
    {
        var env = new DualWriteEnvironment("C1", "contoso", "uat-fo");
        var maps = new List<DualWriteMap> { Map("Vendors", "1.0"), Map("Accounts", "1.0") };

        var json = DualWriteConfigExporter.ExportJson(env, maps, DateTimeOffset.UnixEpoch);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Accounts", doc.RootElement.GetProperty("maps")[0].GetProperty("name").GetString());
    }
}
