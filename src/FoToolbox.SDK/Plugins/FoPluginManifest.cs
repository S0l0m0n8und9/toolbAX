using System.Text.Json.Serialization;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Plugin manifest deserialized from the embedded <c>PluginManifest.json</c> resource.
/// Controls capability gating and SDK version compatibility checks at load time.
/// </summary>
public sealed class FoPluginManifest
{
    /// <summary>Unique plugin identifier (e.g. "fo.querybuilder"). Must be globally unique.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Human-readable display name shown in the host plugin list.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Semantic version of the plugin (e.g. "1.0.0").</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Minimum SDK version required by this plugin (e.g. "0.3.0").
    /// The host rejects plugins whose <c>minSdk</c> exceeds <see cref="SdkInfo.Version"/>.
    /// </summary>
    [JsonPropertyName("minSdk")]
    public required string MinSdk { get; init; }

    /// <summary>
    /// Optional capability strings that control which context the host provides.
    /// Include <c>"OData.Write"</c> to receive <see cref="Plugins.IPluginContextWrite"/>.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyCollection<string> Capabilities { get; init; } = Array.Empty<string>();
}
