using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Host.Plugins;

internal sealed class PluginContextWrite : IPluginContext, IPluginContextWrite, IPluginContextDataverse, IPluginContextNavigation, IPluginContextDualWrite
{
    private readonly PluginNavigationBus _navBus;
    private readonly DataIntegratorCredentialStore? _diStore;
    private readonly DataIntegratorTokenService? _diTokens;

    public PluginContextWrite(FoEnvironment env, IODataClient odata, IODataWriteClient odataWrite, ICatalogService catalog, ILogger logger, DataverseEnvironment? dataverseEnv, HttpClient? dataverseHttp, PluginNavigationBus navBus, DataIntegratorCredentialStore? diStore = null, DataIntegratorTokenService? diTokens = null)
    {
        CurrentEnv = env;
        OData = odata;
        ODataWrite = odataWrite;
        Catalog = catalog;
        Logger = logger;
        CurrentDataverseEnv = dataverseEnv;
        DataverseHttp = dataverseHttp;
        _navBus = navBus;
        _diStore = diStore;
        _diTokens = diTokens;
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public IODataWriteClient ODataWrite { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }
    public bool HasDataverseProfile => CurrentDataverseEnv is not null && DataverseHttp is not null;
    public DataverseEnvironment? CurrentDataverseEnv { get; }
    public HttpClient? DataverseHttp { get; }

    public bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters) =>
        _navBus.TryNavigateTo(targetPluginId, parameters);

    public async Task<string> AcquireDataIntegratorTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_diStore is null || _diTokens is null)
        {
            throw new DualWriteAuthException(
                "Data Integrator credential store is not available in this context.");
        }
        var credential = await _diStore.GetAsync(CurrentEnv.Id, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            throw new DualWriteAuthException(
                "No Data Integrator credential configured for this profile. Set it in Profiles → Data Integrator.");
        }
        return await _diTokens.GetTokenAsync(credential, CurrentEnv.TenantId, cancellationToken).ConfigureAwait(false);
    }
}
