using System;
using FoToolbox.Core.Auth;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Builds a deterministic deep link from a dual-write map to its <c>msdyn_dualwriteentitymap</c> record
/// in the model-driven app, so users can jump from toolbAX straight into the native dual-write map
/// configuration page rather than copying ids by hand.
/// </summary>
public static class DualWriteMapLink
{
    /// <summary>The Dataverse logical name of a dual-write entity map.</summary>
    public const string EntityLogicalName = "msdyn_dualwriteentitymap";

    /// <summary>
    /// The model-driven record URL for a dual-write map, or <see langword="null"/> when it can't be
    /// built — no Dataverse environment URL, or a missing/invalid map id. Never throws.
    /// </summary>
    public static string? BuildMapRecordUrl(string? dataverseBaseUrl, string? mapId)
    {
        if (string.IsNullOrWhiteSpace(dataverseBaseUrl) || string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        // The id must be a real Dataverse record GUID — guard so a malformed value never produces a
        // plausible-but-wrong link.
        if (!Guid.TryParse(mapId, out var id))
        {
            return null;
        }

        var baseUrl = ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(dataverseBaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        // A bare host (no scheme) would produce a relative URL the launcher can't open — default to https.
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl;
        }

        return $"{baseUrl}/main.aspx?etn={EntityLogicalName}&id={id:D}&pagetype=entityrecord";
    }
}
