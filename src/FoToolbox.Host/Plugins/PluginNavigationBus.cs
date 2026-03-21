using FoToolbox.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Host.Plugins;

/// <summary>
/// Shared navigation router passed to every plugin context.  After all plugins are
/// discovered, call <see cref="SetPlugins"/> so that cross-plugin navigation can be
/// resolved. The host should subscribe to <see cref="PluginActivationRequested"/> to
/// bring the target tab into focus.
/// </summary>
public sealed class PluginNavigationBus : IPluginContextNavigation
{
    private IReadOnlyList<LoadedPlugin>? _plugins;

    /// <summary>
    /// Fired on the calling thread when a navigation request is successfully dispatched.
    /// The argument is the <see cref="LoadedPlugin"/> that was navigated to.
    /// The host should use this to activate the corresponding tab.
    /// </summary>
    public event Action<LoadedPlugin>? PluginActivationRequested;

    public void SetPlugins(IReadOnlyList<LoadedPlugin> plugins)
    {
        _plugins = plugins;
    }

    public bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters)
    {
        if (_plugins is null || string.IsNullOrWhiteSpace(targetPluginId))
        {
            return false;
        }

        var target = _plugins.FirstOrDefault(p =>
            string.Equals(p.Manifest.Id, targetPluginId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return false;
        }

        // IFoToolPluginNavigation is defined in FoToolbox.SDK which is loaded from the default
        // AssemblyLoadContext and shared across all plugin contexts — this cast is safe.
        if (target.Instance is not IFoToolPluginNavigation navTarget)
        {
            return false;
        }

        navTarget.OnNavigateTo(parameters);
        PluginActivationRequested?.Invoke(target);
        return true;
    }
}
