using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace FoToolbox.Host.Plugins;

/// <summary>
/// Collectible load context per plugin for isolation and unload.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;
    private readonly string _hostBaseDirectory;

    public PluginLoadContext(string pluginDirectory) : base(isCollectible: true)
    {
        _pluginDirectory = pluginDirectory;
        _hostBaseDirectory = AppContext.BaseDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Prefer an already-loaded assembly from the default context so shared
        // contracts like IFoToolPlugin keep a single type identity.
        var alreadyLoaded = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (alreadyLoaded is not null)
        {
            return alreadyLoaded;
        }

        var candidate = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(candidate))
        {
            return LoadFromAssemblyPath(candidate);
        }

        // Fall back to host-shipped dependencies, but load them into the default
        // context so plugin contracts are shared with the host.
        candidate = Path.Combine(_hostBaseDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(candidate))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }

        return null;
    }
}
