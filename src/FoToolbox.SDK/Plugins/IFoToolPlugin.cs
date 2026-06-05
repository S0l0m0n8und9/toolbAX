using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using Microsoft.Extensions.Logging;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Core plugin contract. Implement this interface to create a FoToolbox plugin.
/// The host discovers implementations via assembly scanning and calls
/// <see cref="InitializeAsync"/> followed by <see cref="CreateTool"/> during startup.
/// </summary>
public interface IFoToolPlugin
{
    /// <summary>Unique plugin identifier (e.g. "fo.querybuilder"). Must match the manifest Id.</summary>
    string Id { get; }

    /// <summary>Plugin version used for compatibility checks.</summary>
    Version Version { get; }

    /// <summary>Deserialized manifest from the embedded <c>PluginManifest.json</c> resource.</summary>
    FoPluginManifest Manifest { get; }

    /// <summary>
    /// Called once after the plugin is instantiated. Store the <paramref name="context"/>
    /// for later use; it provides access to OData, catalog, and logging services.
    /// </summary>
    Task InitializeAsync(IPluginContext context);

    /// <summary>
    /// Creates the view that is displayed as a tab in the host window. The returned
    /// <see cref="IPluginView"/> is adapted by the host to its concrete UI type (the WPF host wraps a
    /// control with <c>FoToolbox.SDK.Wpf.WpfPluginView</c>). Called once after
    /// <see cref="InitializeAsync"/> completes.
    /// </summary>
    IPluginView CreateTool();
}

/// <summary>
/// Read-only runtime context provided to every plugin. Cast to
/// <see cref="IPluginContextWrite"/>, <see cref="IPluginContextDataverse"/>,
/// or <see cref="IPluginContextNavigation"/> for extended capabilities.
/// </summary>
public interface IPluginContext
{
    /// <summary>The active F&amp;O environment. May change on profile switch.</summary>
    FoEnvironment CurrentEnv { get; set; }

    /// <summary>Streaming OData client for read queries against the F&amp;O data endpoint.</summary>
    IODataClient OData { get; }

    /// <summary>Table/entity catalog with metadata caching and ETag support.</summary>
    ICatalogService Catalog { get; }

    /// <summary>Logger scoped to the plugin's execution context.</summary>
    ILogger Logger { get; }
}
