using FoToolbox.SDK.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FoToolbox.Host.Plugins;

internal static class PluginManifestReader
{
    public static FoPluginManifest ReadOrThrow(Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("PluginManifest.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Manifest not found in plugin assembly {assembly.FullName}.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Manifest stream null for {assembly.FullName}.");
        }

        var manifest = JsonSerializer.Deserialize<FoPluginManifest>(stream);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Manifest could not be deserialized in {assembly.FullName}.");
        }

        return manifest;
    }
}
