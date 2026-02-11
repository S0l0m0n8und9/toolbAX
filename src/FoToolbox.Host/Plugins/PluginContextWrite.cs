using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;

namespace FoToolbox.Host.Plugins;

internal sealed class PluginContextWrite : IPluginContext, IPluginContextWrite
{
    public PluginContextWrite(FoEnvironment env, IODataClient odata, IODataWriteClient odataWrite, ICatalogService catalog, ILogger logger)
    {
        CurrentEnv = env;
        OData = odata;
        ODataWrite = odataWrite;
        Catalog = catalog;
        Logger = logger;
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public IODataWriteClient ODataWrite { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }
}

