using FoToolbox.SDK.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace FoToolbox.Host.Plugins;

internal static class PluginManifestReader
{
    private const string ManifestResourceSuffix = "PluginManifest.json";

    /// <summary>
    /// True if the file at <paramref name="assemblyPath"/> is a managed assembly that embeds a plugin
    /// manifest resource (name ending in <c>PluginManifest.json</c>). Reads PE metadata only — no code
    /// runs and nothing is loaded into an <see cref="System.Runtime.Loader.AssemblyLoadContext"/> — so
    /// it is safe to call on arbitrary, untrusted DLLs before any trust decision. Returns false for
    /// anything that cannot be read as a managed assembly carrying such a resource.
    /// </summary>
    public static bool HasManifestResource(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return false;
            }

            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.ManifestResources)
            {
                var name = reader.GetString(reader.GetManifestResource(handle).Name);
                if (name.EndsWith(ManifestResourceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // Unreadable / not a managed assembly → treat as "not a plugin" (never prompts).
            return false;
        }
    }

    public static FoPluginManifest ReadOrThrow(Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ManifestResourceSuffix, StringComparison.OrdinalIgnoreCase));

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
