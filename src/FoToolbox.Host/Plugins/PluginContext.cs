using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FoToolbox.Host.Plugins;

internal sealed class PluginContext : IPluginContext, IPluginContextDataverse
{
    public PluginContext(FoEnvironment env, IODataClient odata, ICatalogService catalog, ILogger logger, DataverseEnvironment? dataverseEnv, HttpClient? dataverseHttp)
    {
        CurrentEnv = env;
        OData = odata;
        Catalog = catalog;
        Logger = logger;
        CurrentDataverseEnv = dataverseEnv;
        DataverseHttp = dataverseHttp;
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }
    public bool HasDataverseProfile => CurrentDataverseEnv is not null && DataverseHttp is not null;
    public DataverseEnvironment? CurrentDataverseEnv { get; }
    public HttpClient? DataverseHttp { get; }
}
