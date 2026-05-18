using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteMapBrowserPlugin;

public static class TestifyRunner
{
    /// <summary>
    /// Attempts to DELETE the entity at <see cref="TestifyMapConfiguration.LastEntityInstanceUrl"/>.
    /// The idempotency metadata is cleared unconditionally before the DELETE so that a failed rollback
    /// does not leave stale URL state that would cause the next run to skip creation.
    /// A 404 response counts as success (entity already gone).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the DELETE returned 2xx or 404; <see langword="false"/> on any other
    /// HTTP failure.  Callers are responsible for re-throwing the original failure exception.
    /// </returns>
    internal static async Task<bool> RollbackAsync(
        IODataWriteClient writeClient,
        TestifyMapConfiguration configuration,
        TestifyConfigurationStore configStore,
        CancellationToken ct = default)
    {
        if (writeClient is null) throw new ArgumentNullException(nameof(writeClient));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configStore is null) throw new ArgumentNullException(nameof(configStore));

        var entityInstanceUrl = configuration.LastEntityInstanceUrl;

        // Clear idempotency metadata unconditionally so the next run starts fresh
        // even when the DELETE request fails (acceptance criterion 3).
        configuration.LastEntityInstanceUrl = null;
        configuration.LastRunToken = null;
        await configStore.SaveAsync(configuration, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(entityInstanceUrl))
        {
            return true;
        }

        var response = await writeClient.SendAsync(
            new ODataWriteRequest(HttpMethod.Delete, entityInstanceUrl), ct).ConfigureAwait(false);

        return (response.StatusCode >= 200 && response.StatusCode <= 299) || response.StatusCode == 404;
    }


    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildEnumMembersByTypeLookup(IReadOnlyDictionary<string, ODataEnumType> enumLookup)
    {
        return enumLookup
            .Values
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.First().Members, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryBuildPayload(
        ODataEntity entity,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> enumMembersByType,
        bool enforceMandatory,
        out string json,
        out IReadOnlyList<string> issues)
    {
        var fields = values.Select(v => new ODataFieldValue(v.Key, Include: true, v.Value)).ToList();
        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields, enumMembersByType, enforceMandatory: enforceMandatory);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Json))
        {
            json = string.Empty;
            issues = result.Issues;
            return false;
        }

        json = result.Json;
        issues = Array.Empty<string>();
        return true;
    }

    public static bool TryBuildEntityInstanceUrl(
        string collectionUrl,
        ODataEntity entity,
        IReadOnlyDictionary<string, string> values,
        out string instanceUrl,
        out string error)
    {
        instanceUrl = string.Empty;
        error = string.Empty;

        var keys = entity.Properties.Where(p => p.IsKey).ToList();
        if (keys.Count == 0)
        {
            error = $"Entity '{entity.Name}' does not expose key metadata.";
            return false;
        }

        var parts = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key.Name, out var keyValue) || string.IsNullOrWhiteSpace(keyValue))
            {
                error = $"Missing key value '{key.Name}' for PATCH URL.";
                return false;
            }

            var literal = BuildODataLiteral(key.Type, keyValue);
            parts.Add($"{key.Name}={literal}");
        }

        var baseUrl = collectionUrl.TrimEnd('/');
        instanceUrl = $"{baseUrl}({string.Join(",", parts)})?cross-company=true";
        return true;
    }

    private static string BuildODataLiteral(string type, string value)
    {
        return type switch
        {
            "Edm.Boolean" => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            "Edm.Int16" or "Edm.Int32" or "Edm.Int64" or "Edm.Decimal" or "Edm.Double" or "Edm.Single" => value,
            "Edm.Guid" => Guid.TryParse(value, out var parsed)
                ? parsed.ToString("D", CultureInfo.InvariantCulture)
                : $"'{EscapeString(value)}'",
            _ => $"'{EscapeString(value)}'"
        };
    }

    private static string EscapeString(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}