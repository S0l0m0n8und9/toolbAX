using System.Collections.Generic;
using System.Threading;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoToolbox.TestHelpers;

/// <summary>
/// Minimal in-memory <see cref="IPluginContext"/> for unit-testing plugin view models without a host.
/// Defaults to the seeded <see cref="FakeCatalogService"/> and an empty OData client; pass a custom
/// catalog (e.g. one that completes asynchronously) to exercise specific flows.
/// </summary>
public sealed class FakePluginContext : IPluginContext
{
    public FakePluginContext(
        ICatalogService? catalog = null,
        FoEnvironment? env = null,
        IODataClient? oData = null,
        ILogger? logger = null)
    {
        Catalog = catalog ?? new FakeCatalogService();
        CurrentEnv = env ?? new FoEnvironment(
            "dev", "Dev", "https://contoso.operations.dynamics.com",
            "00000000-0000-0000-0000-000000000000", "USMF");
        OData = oData ?? new EmptyODataClient();
        Logger = logger ?? NullLogger.Instance;
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }

    private sealed class EmptyODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }
}
