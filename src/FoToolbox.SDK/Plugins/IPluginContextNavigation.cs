using System.Collections.Generic;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional capability that allows a plugin to navigate to another plugin by ID,
/// passing a set of named parameters. Plugins should cast <see cref="IPluginContext"/>
/// to this interface before use and degrade gracefully if the host does not support it.
/// </summary>
public interface IPluginContextNavigation
{
    /// <summary>
    /// Requests the host to activate the plugin identified by <paramref name="targetPluginId"/>
    /// and pass <paramref name="parameters"/> to it.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the target plugin was found and navigation was dispatched;
    /// <c>false</c> if the target plugin is not loaded or does not support navigation.
    /// </returns>
    bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters);
}
