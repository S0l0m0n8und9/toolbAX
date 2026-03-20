using System.Collections.Generic;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional interface for plugins that can receive inbound navigation requests from other
/// plugins via <see cref="IPluginContextNavigation.TryNavigateTo"/>.
/// </summary>
public interface IFoToolPluginNavigation
{
    /// <summary>
    /// Called by the host when another plugin has requested navigation to this plugin.
    /// The implementation should inspect <paramref name="parameters"/> and update its
    /// own state accordingly (e.g. pre-select an entity).
    /// </summary>
    void OnNavigateTo(IReadOnlyDictionary<string, string> parameters);
}
