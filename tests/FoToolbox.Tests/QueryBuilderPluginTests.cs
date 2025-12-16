using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilderPlugin;
using QB = QueryBuilderPlugin;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class QueryBuilderPluginTests
{
    private sealed class FakeContext : IPluginContext
    {
        public FakeContext()
        {
            CurrentEnv = new FoToolbox.Core.Models.FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = new FakeODataClient();
            Logger = NullLogger.Instance;
        }

        public FoToolbox.Core.Models.FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
    }

    private sealed class FakeODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, System.Threading.CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }

    [Fact]
#pragma warning disable xUnit1031 // STA thread requires blocking init
    public void Plugin_Initializes_And_Creates_View()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var plugin = new QB.QueryBuilderPlugin(new FakeMetadataProvider());
                plugin.InitializeAsync(new FakeContext()).GetAwaiter().GetResult();
                Assert.Equal("fo.querybuilder", plugin.Id);
                Assert.Equal("fo.querybuilder", plugin.Manifest.Id);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null) throw failure;
    }
#pragma warning restore xUnit1031

    private sealed class FakeMetadataProvider : IMetadataProvider
    {
        public Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            var entity = new ODataEntity("Customers", new[] { new ODataProperty("AccountNumber", "Edm.String", false), new ODataProperty("Name", "Edm.String", true) }, Array.Empty<ODataNavigationProperty>());
            return Task.FromResult(new ODataMetadata(new[] { entity }, null));
        }
    }
}
