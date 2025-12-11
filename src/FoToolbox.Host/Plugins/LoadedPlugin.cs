using FoToolbox.SDK.Plugins;
using System.Runtime.Loader;
using System.Windows.Controls;

namespace FoToolbox.Host.Plugins;

public sealed class LoadedPlugin
{
    public required IFoToolPlugin Instance { get; init; }
    public required FoPluginManifest Manifest { get; init; }
    public required UserControl ToolControl { get; init; }
    public required PluginLoadContext LoadContext { get; init; }
}
