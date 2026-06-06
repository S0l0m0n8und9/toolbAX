using System.Collections.Generic;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>The catalogue of available tools/plugins shown on the Plugins home (control-map §1).</summary>
public interface IPluginCatalog
{
    IReadOnlyList<PluginCard> Plugins { get; }
}
