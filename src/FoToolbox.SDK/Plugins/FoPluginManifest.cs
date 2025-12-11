using System.Text.Json.Serialization;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Basic plugin manifest used for capability and version gating.
/// </summary>
public sealed class FoPluginManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("minSdk")]
    public required string MinSdk { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyCollection<string> Capabilities { get; init; } = Array.Empty<string>();
}
