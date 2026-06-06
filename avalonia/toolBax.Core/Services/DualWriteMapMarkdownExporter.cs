using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Renders a <see cref="DwMapRecord"/> to Markdown for the Map Browser's export, mirroring the WPF
/// plugin's section layout (details, legs, fields, value transforms, properties, raw JSON). Pure and
/// side-effect-free — writing the file is the caller's job via <see cref="IFileSaveService"/>.
/// </summary>
public static class DualWriteMapMarkdownExporter
{
    private const string MissingValue = "(not set)";

    public static string Export(DwMapRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var builder = new StringBuilder();
        builder.AppendLine($"# {MarkdownEscape(ValueOrPlaceholder(record.DisplayName))}");
        builder.AppendLine();
        builder.AppendLine("## Map Details");
        builder.AppendLine();
        AppendKeyValue(builder, "Display Name", record.DisplayName);
        AppendKeyValue(builder, "Name", record.Name);
        AppendKeyValue(builder, "Map ID", record.Id);
        AppendKeyValue(builder, "Solution ID", record.SolutionId);
        AppendKeyValue(builder, "Version", record.Version);
        AppendKeyValue(builder, "State", record.State);
        AppendKeyValue(builder, "Status", record.Status);
        AppendKeyValue(builder, "Owner", record.Owner);
        AppendKeyValue(builder, "Created", record.CreatedOnLabel);
        AppendKeyValue(builder, "Modified", record.ModifiedOnLabel);
        builder.AppendLine();

        AppendTwoColumnTable(builder, "Mapping Summary", "Key", "Value", record.SummaryRows, r => r.Key, r => r.Value);
        AppendMappingLegs(builder, record.Legs);
        AppendMappingFields(builder, record.Fields);
        AppendValueTransforms(builder, record.ValueTransforms);
        AppendProperties(builder, record.Properties);
        AppendCodeBlock(builder, "Raw Mapping JSON", record.RawMapping);
        AppendCodeBlock(builder, "Raw Properties JSON", record.RawProperties);

        return builder.ToString();
    }

    /// <summary>A safe ".md" file name derived from the map's display name (falling back to name/id).</summary>
    public static string SuggestedFileName(DwMapRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var basis = record.Title;
        var sanitized = new string(basis.Select(c => InvalidFileNameChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "dual-write-map";
        }

        return $"{sanitized}.md";
    }

    // A conservative cross-platform set (Path.GetInvalidFileNameChars differs per OS; the Map Browser
    // is the same app on Windows + Linux, so we sanitize the union of the usual offenders).
    private static readonly HashSet<char> InvalidFileNameChars = new(
        new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }.Concat(
            Enumerable.Range(0, 32).Select(i => (char)i)));

    private static void AppendMappingLegs(StringBuilder builder, IReadOnlyList<DwMapLeg> rows)
    {
        AppendSectionHeader(builder, "Mapping Legs");
        builder.AppendLine("| Leg | Source Schema | Source Distinct Name | Destination Schema | Source Env | Destination Env | Source Filter | Reversed Filter | Field Mappings |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine($"| {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | 0 |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {Cell(row.LegId)} | {Cell(row.SourceSchema)} | {Cell(row.SourceSchemaDistinctName)} | {Cell(row.DestinationSchema)} | {Cell(row.SourceEnvironmentType)} | {Cell(row.DestinationEnvironmentType)} | {Cell(row.SourceFilter)} | {Cell(row.ReversedSourceFilter)} | {row.FieldMappings.ToString(CultureInfo.InvariantCulture)} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendMappingFields(StringBuilder builder, IReadOnlyList<DwMapField> rows)
    {
        AppendSectionHeader(builder, "Mapping Fields");
        builder.AppendLine("| Leg | Sync Direction | Source Field | Destination Field | Lookup Entity | System Generated | Value Transforms | Source Schema | Destination Schema |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine($"| {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | 0 | {MissingValue} | {MissingValue} |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {Cell(row.LegId)} | {Cell(row.SyncDirection)} | {Cell(row.SourceField)} | {Cell(row.DestinationField)} | {Cell(row.DestinationLookupEntity)} | {Cell(FormatNullableBool(row.IsSystemGenerated))} | {row.ValueTransforms.ToString(CultureInfo.InvariantCulture)} | {Cell(row.SourceSchema)} | {Cell(row.DestinationSchema)} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendValueTransforms(StringBuilder builder, IReadOnlyList<DwMapValueTransform> rows)
    {
        AppendSectionHeader(builder, "Value Transforms");
        builder.AppendLine("| Leg | Source Field | Destination Field | Transform Type | Default Value | Value Map | Create Values On Destination |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine($"| {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} | {MissingValue} |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {Cell(row.LegId)} | {Cell(row.SourceField)} | {Cell(row.DestinationField)} | {Cell(row.TransformType)} | {Cell(row.DefaultValue)} | {Cell(row.ValueMap)} | {Cell(FormatNullableBool(row.CreateValuesOnDestination))} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendProperties(StringBuilder builder, IReadOnlyList<DwMapProperty> rows)
    {
        AppendSectionHeader(builder, "Properties");
        builder.AppendLine("| Key | Type | Value |");
        builder.AppendLine("| --- | --- | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine($"| {MissingValue} | {MissingValue} | {MissingValue} |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {Cell(row.Key)} | {Cell(row.Type)} | {Cell(row.Value)} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendTwoColumnTable<T>(StringBuilder builder, string title, string firstColumn,
        string secondColumn, IReadOnlyList<T> rows, Func<T, string> firstSelector, Func<T, string> secondSelector)
    {
        AppendSectionHeader(builder, title);
        builder.AppendLine($"| {firstColumn} | {secondColumn} |");
        builder.AppendLine("| --- | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine($"| {MissingValue} | {MissingValue} |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {Cell(firstSelector(row))} | {Cell(secondSelector(row))} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendCodeBlock(StringBuilder builder, string title, string? value)
    {
        AppendSectionHeader(builder, title);
        builder.AppendLine("```json");
        builder.AppendLine(ValueOrPlaceholder(value));
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static void AppendSectionHeader(StringBuilder builder, string title)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
    }

    private static void AppendKeyValue(StringBuilder builder, string key, string? value) =>
        builder.AppendLine($"- **{MarkdownEscape(key)}:** {MarkdownEscape(ValueOrPlaceholder(value))}");

    private static string Cell(string? value) => MarkdownEscape(ValueOrPlaceholder(value));

    private static string ValueOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ? MissingValue : value;

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? (value.Value ? "true" : "false") : MissingValue;

    private static string MarkdownEscape(string value) => value
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r\n", "<br>", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal)
        .Replace("\r", "<br>", StringComparison.Ordinal);
}
