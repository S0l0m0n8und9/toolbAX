using System;
using System.Collections.Generic;
using System.Text.Json;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Reshapes a Dataverse <c>EntityDefinitions</c> Web API response into <see cref="VirtualTableInfo"/>s,
/// keeping only <b>virtual</b> tables (those with an external data provider) and classifying each as
/// finance-and-operations-backed or other. Pure and deterministic — no I/O — so it's fully unit-testable.
///
/// F&amp;O virtual entities are identified by the documented <c>mserp_</c> logical-name prefix that the
/// MicrosoftOperationsERPVE solution stamps on every generated F&amp;O virtual table; a table is treated
/// as virtual when it carries a non-empty <c>DataProviderId</c> or an <c>ExternalName</c>.
/// </summary>
public static class VirtualTableMetadataParser
{
    /// <summary>Prefix the F&amp;O virtual-entity provider gives every generated table's logical name.</summary>
    public const string FoLogicalNamePrefix = "mserp_";

    /// <summary>The columns to request from <c>EntityDefinitions</c> for the Virtual Tables screen.</summary>
    public const string SelectColumns =
        "LogicalName,DisplayName,ExternalName,ExternalCollectionName,DataProviderId,DataSourceId,IsManaged";

    private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

    public static IReadOnlyList<VirtualTableInfo> Parse(string? json)
    {
        var tables = new List<VirtualTableInfo>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return tables;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return tables;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return tables;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var logicalName = ReadString(item, "LogicalName");
                var externalName = ReadString(item, "ExternalName");
                var dataProviderId = ReadGuid(item, "DataProviderId");
                var dataSourceId = ReadGuid(item, "DataSourceId");

                // Virtual = backed by an external data provider. Physical tables have neither.
                var isVirtual = dataProviderId.Length > 0 || externalName.Length > 0;
                if (!isVirtual)
                {
                    continue;
                }

                var source = logicalName.StartsWith(FoLogicalNamePrefix, StringComparison.OrdinalIgnoreCase)
                    ? VirtualTableSource.FinanceAndOperations
                    : VirtualTableSource.Other;

                tables.Add(new VirtualTableInfo(
                    LogicalName: logicalName,
                    DisplayName: ReadDisplayName(item),
                    ExternalName: externalName,
                    ExternalCollectionName: ReadString(item, "ExternalCollectionName"),
                    DataProviderId: dataProviderId,
                    DataSourceId: dataSourceId,
                    IsManaged: ReadBool(item, "IsManaged"),
                    Source: source));
            }
        }

        return tables;
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    // A null/empty/all-zero GUID means "not set" — normalize all three to empty.
    private static string ReadGuid(JsonElement item, string name)
    {
        var raw = ReadString(item, name);
        return string.IsNullOrWhiteSpace(raw) || string.Equals(raw, EmptyGuid, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : raw;
    }

    private static bool ReadBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    // DisplayName is a LocalizedLabel: { "UserLocalizedLabel": { "Label": "..." }, "LocalizedLabels": [...] }.
    private static string ReadDisplayName(JsonElement item)
    {
        if (item.TryGetProperty("DisplayName", out var dn)
            && dn.ValueKind == JsonValueKind.Object
            && dn.TryGetProperty("UserLocalizedLabel", out var ull)
            && ull.ValueKind == JsonValueKind.Object
            && ull.TryGetProperty("Label", out var label)
            && label.ValueKind == JsonValueKind.String)
        {
            return label.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
