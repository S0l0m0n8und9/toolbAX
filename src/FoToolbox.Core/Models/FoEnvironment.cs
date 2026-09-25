using System.Text.Json.Serialization;

namespace FoToolbox.Core.Models;

/// <summary>
/// Basic environment profile for connecting to a D365 F&O instance.
/// </summary>
public record FoEnvironment(
    string Id,
    string Name,
    string BaseUrl,
    string TenantId,
    string? DefaultCompany)
{
    /// <summary>
    /// Optional request-context discriminator for metadata caches only. Never changes the actual profile
    /// ID, routing, authentication, or persisted profile. Unset preserves the Core catalog's legacy scope.
    /// </summary>
    [JsonIgnore]
    public string? MetadataCachePartition { get; init; }
}
