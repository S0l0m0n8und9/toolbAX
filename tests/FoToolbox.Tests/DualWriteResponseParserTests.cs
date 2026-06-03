using System.Linq;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

/// <summary>
/// Tests <see cref="DualWriteResponseParser.ParseMaps"/> against the real Dual-write
/// Management gateway <c>Entities</c> response shape (captured live): the map fields live
/// under <c>leftEntity</c>/<c>rightEntity</c>/<c>detail</c>, the version is an object
/// (<c>{major,minor,build,revision}</c>), and the state is a numeric <c>MapStatus</c> code.
/// </summary>
public class DualWriteResponseParserTests
{
    // Trimmed from a live response. Two maps share the same leftEntity ("Customers V3")
    // but target different CE tables (accounts, contacts) — so the map identity must be the
    // composite name, not the F&O entity alone.
    private const string RealEntitiesJson = """
    [
      {
        "leftEntity": { "targetType": "AX", "name": "Customers V3", "displayName": "Customers V3", "singletonName": "Customers V3" },
        "rightEntity": { "targetType": "CRM", "name": "accounts", "displayName": "accounts", "singletonName": "account" },
        "detail": {
          "tid": "78f8df1b-b02c-414e-85a5-6ce51f030d02",
          "tName": "accounts - Customers V3",
          "templates": [
            { "id": "78f8df1b-b02c-414e-85a5-6ce51f030d02", "name": "fus_[accounts - Customers V3]", "author": "Fusion5", "version": { "major": 2, "minor": 0, "build": 1, "revision": 0 } },
            { "id": "643d0f02-5c3c-4e81-b30d-af08a693bd24", "name": "fus_[accounts - Customers V3]", "author": "Fusion5", "version": { "major": 2, "minor": 0, "build": 0, "revision": 0 } }
          ],
          "template": { "id": "78f8df1b-b02c-414e-85a5-6ce51f030d02", "name": "fus_[accounts - Customers V3]", "author": "Fusion5", "version": { "major": 2, "minor": 0, "build": 1, "revision": 0 } },
          "pid": "50265db7-466d-4fc6-abd0-b40ea065b511",
          "state": "4",
          "actions": ["4", "5"]
        }
      },
      {
        "leftEntity": { "targetType": "AX", "name": "Customers V3", "displayName": "Customers V3", "singletonName": "Customers V3" },
        "rightEntity": { "targetType": "CRM", "name": "contacts", "displayName": "contacts", "singletonName": "contact" },
        "detail": {
          "tid": "e5feddfb-131b-490b-9927-5ec9e686b02c",
          "tName": "contacts - Customers V3",
          "templates": [
            { "id": "e5feddfb-131b-490b-9927-5ec9e686b02c", "name": "fus_[contacts - Customers V3]", "author": "Fusion5", "version": { "major": 1, "minor": 0, "build": 0, "revision": 0 } }
          ],
          "template": { "id": "e5feddfb-131b-490b-9927-5ec9e686b02c", "name": "fus_[contacts - Customers V3]", "author": "Fusion5", "version": { "major": 1, "minor": 0, "build": 0, "revision": 0 } },
          "pid": "8dceb1cd-e5da-468f-be08-b976901ad302",
          "state": "1"
        }
      }
    ]
    """;

    [Trait("Category", "DualWrite")]
    [Fact]
    public void ParseMaps_RealEntitiesShape_PopulatesAllDisplayedColumns()
    {
        var maps = DualWriteResponseParser.ParseMaps(RealEntitiesJson);

        Assert.Equal(2, maps.Count);
        var accounts = maps[0];

        // CE Entity column (was the only thing populating before the fix).
        Assert.Equal("accounts", accounts.RightEntityName);
        // Map column shows the F&O (left) entity name; the unique composite stays in Name.
        Assert.Equal("Customers V3", accounts.DisplayName);
        Assert.Equal("accounts - Customers V3", accounts.Name);
        // Version column — formatted from the version object, not blank.
        Assert.Equal("2.0.1.0", accounts.CurrentVersion);
        // Author column.
        Assert.Equal("Fusion5", accounts.CurrentAuthor);
        // State column — friendly name from the numeric MapStatus code "4".
        Assert.Equal("Running", accounts.State);
        // Project id drives lifecycle actions.
        Assert.Equal("50265db7-466d-4fc6-abd0-b40ea065b511", accounts.ProjectId);
        // Id must be the active template id so Start/Stop send a valid tid.
        Assert.Equal("78f8df1b-b02c-414e-85a5-6ce51f030d02", accounts.Id);
        Assert.Equal(2, accounts.Templates.Count);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void ParseMaps_DuplicateLeftEntity_ProducesDistinctMapNames()
    {
        var maps = DualWriteResponseParser.ParseMaps(RealEntitiesJson);

        // Both maps share leftEntity "Customers V3"; the names must still be distinct so the
        // comparer/exporter don't collapse them.
        Assert.Equal("accounts - Customers V3", maps[0].Name);
        Assert.Equal("contacts - Customers V3", maps[1].Name);
    }

    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData("0", "None")]
    [InlineData("1", "Stopped")]
    [InlineData("2", "Initial sync")]
    [InlineData("3", "Catch-up")]
    [InlineData("4", "Running")]
    [InlineData("5", "Paused")]
    [InlineData("6", "Not running")]
    [InlineData("Running", "Running")]
    [InlineData("7", "7")] // unknown code surfaces verbatim, never silently blanked
    public void ParseMaps_MapStatusCode_MapsToFriendlyState(string stateCode, string expected)
    {
        var json = $$"""
        [
          {
            "leftEntity": { "name": "E", "displayName": "E" },
            "rightEntity": { "name": "ce" },
            "detail": { "tName": "ce - E", "pid": "p", "state": "{{stateCode}}",
              "template": { "id": "t", "author": "A", "version": { "major": 1, "minor": 0, "build": 0, "revision": 0 } },
              "templates": [] }
          }
        ]
        """;

        var map = Assert.Single(DualWriteResponseParser.ParseMaps(json));
        Assert.Equal(expected, map.State);
    }

    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"major\": 0, \"minor\": 0, \"build\": 0, \"revision\": 0 }")]
    public void ParseMaps_EmptyOrZeroVersionObject_YieldsBlankVersion(string versionJson)
    {
        // A missing/empty version object is "no version" — it must read blank, not a misleading
        // "0.0.0.0" that looks like a real version (and matches the blank an absent version key gives).
        var json = $$"""
        [
          {
            "leftEntity": { "name": "E", "displayName": "E" },
            "rightEntity": { "name": "ce" },
            "detail": { "tName": "ce - E", "pid": "p", "state": "4",
              "template": { "id": "t", "author": "A", "version": {{versionJson}} },
              "templates": [] }
          }
        ]
        """;

        var map = Assert.Single(DualWriteResponseParser.ParseMaps(json));
        Assert.Equal(string.Empty, map.CurrentVersion);
    }
}
