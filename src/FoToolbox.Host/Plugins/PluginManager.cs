using FoToolbox.Core.Models;
using FoToolbox.SDK;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FoToolbox.Host.Plugins;

/// <summary>
/// Discovers and loads plugins from a directory.
/// </summary>
public sealed class PluginManager
{
    private readonly string _pluginRoot;
    private readonly FoEnvironment _env;
    private readonly IODataClient _odata;
    private readonly ILogger _logger;

    public PluginManager(string pluginRoot, FoEnvironment env, IODataClient odata, ILogger logger)
    {
        _pluginRoot = pluginRoot;
        _env = env;
        _odata = odata;
        _logger = logger;
    }

    public IReadOnlyList<LoadedPlugin> Discover()
    {
        var results = new List<LoadedPlugin>();

        if (!Directory.Exists(_pluginRoot))
        {
            _logger.LogWarning("Plugin directory {Root} not found.", _pluginRoot);
            return results;
        }

        foreach (var dll in Directory.GetFiles(_pluginRoot, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var loaded = LoadPlugin(dll);
                if (loaded is not null)
                {
                    results.Add(loaded);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin {Dll}", dll);
            }
        }

        return results;
    }

    private LoadedPlugin? LoadPlugin(string assemblyPath)
    {
        var loadContext = new PluginLoadContext(Path.GetDirectoryName(assemblyPath)!);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        var manifest = PluginManifestReader.ReadOrThrow(assembly);
        ValidateManifest(manifest);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IFoToolPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

        if (pluginType is null)
        {
            throw new InvalidOperationException($"No IFoToolPlugin implementation found in {assemblyPath}.");
        }

        var plugin = Activator.CreateInstance(pluginType) as IFoToolPlugin
                     ?? throw new InvalidOperationException($"Could not create instance of {pluginType.FullName}.");

        var ctx = new PluginContext(_env, _odata, _logger);
        plugin.InitializeAsync(ctx).GetAwaiter().GetResult();
        var control = plugin.CreateTool();

        return new LoadedPlugin
        {
            Instance = plugin,
            Manifest = manifest,
            ToolControl = control,
            LoadContext = loadContext
        };
    }

    internal static void ValidateManifest(FoPluginManifest manifest)
    {
        if (!Version.TryParse(manifest.MinSdk, out var minSdk))
        {
            throw new InvalidOperationException($"Invalid minSdk '{manifest.MinSdk}' for plugin {manifest.Id}.");
        }

        if (minSdk > SdkInfo.Version)
        {
            throw new InvalidOperationException($"Plugin {manifest.Id} requires SDK {manifest.MinSdk} but host has {SdkInfo.Version}.");
        }
    }
}
