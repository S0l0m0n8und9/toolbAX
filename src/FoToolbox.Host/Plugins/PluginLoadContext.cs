using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace FoToolbox.Host.Plugins;

/// <summary>
/// Collectible load context per plugin for isolation and unload.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginDirectory) : base(isCollectible: true)
    {
        _pluginDirectory = pluginDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var candidate = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(candidate))
        {
            return LoadFromAssemblyPath(candidate);
        }

        return null;
    }
}
