using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;

namespace FoToolbox.Host.Plugins;

internal sealed class PluginContext : IPluginContext
{
    public PluginContext(FoEnvironment env, IODataClient odata, ICatalogService catalog, ILogger logger)
    {
        CurrentEnv = env;
        OData = odata;
        Catalog = catalog;
        Logger = logger;
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }
}
