using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using Microsoft.Extensions.Logging;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Minimal plugin contract exposed to external tools.
/// </summary>
public interface IFoToolPlugin
{
    string Id { get; }
    Version Version { get; }
    FoPluginManifest Manifest { get; }
    Task InitializeAsync(IPluginContext context);
    System.Windows.Controls.UserControl CreateTool();
}

public interface IPluginContext
{
    FoEnvironment CurrentEnv { get; set; }
    IODataClient OData { get; }
    ILogger Logger { get; }
}
