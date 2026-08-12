using System.Linq;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="DualWriteMapMarkdownExporter"/> renders a dual-write map to Markdown (the Map
/// Browser's export). Pure logic → runs on Linux CI. Mirrors the WPF exporter's section layout.
/// </summary>
public class DualWriteMapMarkdownExporterTests
{
    private static DwMapRecord Record(string json) => DualWriteMapParser.ParsePage(json).Records.Single();

    private const string SampleJson = """
    { "value": [ {
        "msdyn_dualwriteentitymapid": "abc-123",
        "msdyn_name": "vend_account",
        "msdyn_displayname": "Vendors to Accounts",
        "msdyn_version": "1.0.0.8",
        "statecode@OData.Community.Display.V1.FormattedValue": "Active",
        "msdyn_mapping": "{\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"VendV2\",\"destinationSchema\":\"accounts\",\"sourceFilter\":\"a | b\",\"fieldMappings\":[{\"sourceField\":\"AccountNum\",\"destinationField\":\"accountnumber\",\"syncDirection\":\"Forward\",\"valueTransforms\":[{\"sourceField\":\"G\",\"destinationField\":\"g\",\"transformType\":\"ValueMap\",\"valueMap\":{\"10\":\"x\"}}]}]}]}",
        "msdyn_properties": "{\"IntegrationKey\":\"AccountNum\"}"
    } ] }
    """;

    [Fact]
    public void Export_starts_with_the_display_name_title_and_map_details()
    {
        var md = DualWriteMapMarkdownExporter.Export(Record(SampleJson));

        Assert.StartsWith("# Vendors to Accounts", md);
        Assert.Contains("## Map Details", md);
        Assert.Contains("- **Name:** vend_account", md);
        Assert.Contains("- **Map ID:** abc-123", md);
        Assert.Contains("- **Version:** 1.0.0.8", md);
        Assert.Contains("- **State:** Active", md);
    }

    [Fact]
    public void Export_renders_the_section_tables()
    {
        var md = DualWriteMapMarkdownExporter.Export(Record(SampleJson));

        Assert.Contains("## Mapping Legs", md);
        Assert.Contains("VendV2", md);
        Assert.Contains("## Mapping Fields", md);
        Assert.Contains("AccountNum", md);
        Assert.Contains("## Value Transforms", md);
        Assert.Contains("ValueMap", md);
        Assert.Contains("## Properties", md);
        Assert.Contains("IntegrationKey", md);
    }

    [Fact]
    public void Export_includes_raw_json_code_blocks()
    {
        var md = DualWriteMapMarkdownExporter.Export(Record(SampleJson));

        Assert.Contains("## Raw Mapping JSON", md);
        Assert.Contains("```json", md);
        Assert.Contains("## Raw Properties JSON", md);
    }

    [Fact]
    public void Export_escapes_pipes_in_table_cells()
    {
        var md = DualWriteMapMarkdownExporter.Export(Record(SampleJson));

        // The leg's sourceFilter "a | b" must not break the table layout.
        Assert.Contains("a \\| b", md);
    }

    [Fact]
    public void Export_uses_a_placeholder_for_missing_values()
    {
        var md = DualWriteMapMarkdownExporter.Export(Record(
            """{ "value": [ { "msdyn_dualwriteentitymapid": "x", "msdyn_name": "n" } ] }"""));

        Assert.Contains("(not set)", md); // empty display name / owner / etc.
    }

    [Fact]
    public void Export_widens_the_fence_when_raw_json_contains_backticks()
    {
        // RawMapping containing a ``` run must not prematurely close the fenced code block.
        var md = DualWriteMapMarkdownExporter.Export(Record(
            """{ "value": [ { "msdyn_dualwriteentitymapid": "x", "msdyn_name": "n", "msdyn_mapping": "{\"note\":\"```\"}" } ] }"""));

        Assert.Contains("````json", md); // fence widened beyond the embedded triple-backtick
    }

    [Fact]
    public void Export_titles_a_display_name_less_map_with_its_logical_name_not_a_placeholder()
    {
        // A map carrying only msdyn_name used to export as "# (not set)" inside a file correctly named
        // after the logical name — the H1 read DisplayName while the file name read the effective title.
        var record = Record("""{ "value": [ { "msdyn_dualwriteentitymapid": "x", "msdyn_name": "vend_account" } ] }""");

        var md = DualWriteMapMarkdownExporter.Export(record);

        Assert.StartsWith("# vend_account", md);
        Assert.DoesNotContain("# (not set)", md);
        // The H1 and the suggested file name now answer to the same title.
        Assert.Equal("vend_account.md", DualWriteMapMarkdownExporter.SuggestedFileName(record));
    }

    [Fact]
    public void Export_falls_back_to_the_map_id_when_it_has_neither_name()
    {
        // Last rung of the shared chain: display name → logical name → id. Still never "# (not set)".
        var md = DualWriteMapMarkdownExporter.Export(Record(
            """{ "value": [ { "msdyn_dualwriteentitymapid": "abc-123" } ] }"""));

        Assert.StartsWith("# abc-123", md);
    }

    [Fact]
    public void SuggestedFileName_is_a_sanitized_md_file()
    {
        var name = DualWriteMapMarkdownExporter.SuggestedFileName(Record(
            """{ "value": [ { "msdyn_dualwriteentitymapid": "x", "msdyn_displayname": "A/B: map*?" } ] }"""));

        Assert.EndsWith(".md", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("*", name);
        Assert.DoesNotContain("?", name);
        Assert.DoesNotContain(":", name);
    }
}
