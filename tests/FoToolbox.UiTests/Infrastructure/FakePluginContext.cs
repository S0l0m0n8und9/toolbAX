using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using FoToolbox.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Seeded fake plugin context implementing every optional capability so any plugin's
/// CreateTool succeeds and bindings have realistic data to resolve against.
/// </summary>
internal sealed class FakePluginContext :
    IPluginContext, IPluginContextWrite, IPluginContextDataverse, IPluginContextNavigation
{
    public FakePluginContext()
    {
        CurrentEnv = new FoEnvironment("env", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        OData = new SeededODataClient();
        Catalog = new FakeCatalogService();
        Logger = NullLogger.Instance;
        ODataWrite = new NoopWriteClient();
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }

    // IPluginContextWrite
    public IODataWriteClient ODataWrite { get; }

    // IPluginContextDataverse
    public bool HasDataverseProfile => false;
    public DataverseEnvironment? CurrentDataverseEnv => null;
    public HttpClient? DataverseHttp => null;

    // IPluginContextNavigation
    public bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters) => false;

    private sealed class SeededODataClient : IODataClient
    {
        public async IAsyncEnumerable<ODataPage> StreamAsync(
            QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var rows = new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["AccountNumber"] = "US-001", ["Name"] = "Contoso Retail" },
                new Dictionary<string, object?> { ["AccountNumber"] = "US-002", ["Name"] = "Fabrikam" },
            };
            yield return new ODataPage(rows, NextLink: null, ODataCount: rows.Length);
        }
    }

    private sealed class NoopWriteClient : IODataWriteClient
    {
        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(new ODataWriteResponse(200, "{}", new Dictionary<string, string>()));
    }
}
