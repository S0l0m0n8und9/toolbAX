using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class CoreVirtualTableReaderTests
{
    private sealed class Client(string body) : IDataverseClient
    {
        public Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default) =>
            Task.FromResult(new ODataResponse(200, "OK", body, 1));
    }

    private sealed class PagingClient(params ODataResponse[] responses) : IDataverseClient
    {
        private readonly Queue<ODataResponse> _responses = new(responses);
        public List<string> Requested { get; } = new();

        public Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Requested.Add(pathOrUrl);
            if (_responses.Count == 0) throw new InvalidOperationException("dispatch limit exceeded");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static EnvProfile Env() => new(
        "env", "Env", "https://fo.example", "tenant", "USMF", "Tier 1", EnvStatus.Connected,
        DataverseUrl: "https://dv.example");

    [Fact]
    public async Task Malformed_success_body_is_a_failure_not_empty_success()
    {
        var result = await new CoreVirtualTableReader(new Client("{}"))
            .GetVirtualTablesAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Tables);
        Assert.Contains("value", result.Error);
    }

    [Fact]
    public async Task Valid_empty_collection_is_success()
    {
        var result = await new CoreVirtualTableReader(new Client("{\"value\":[]}"))
            .GetVirtualTablesAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Tables);
    }

    [Fact]
    public async Task Follows_a_relative_continuation_after_an_empty_physical_only_page()
    {
        var first = "{\"value\":[{\"LogicalName\":\"account\"}],\"@odata.nextLink\":\"EntityDefinitions?$skiptoken=Case\"}";
        var second = "{\"value\":[{\"LogicalName\":\"mserp_customer\",\"ExternalName\":\"Customer\"}]}";
        var client = new PagingClient(
            new ODataResponse(200, "OK", first, 1),
            new ODataResponse(200, "OK", second, 1));

        var result = await new CoreVirtualTableReader(client, Env)
            .GetVirtualTablesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Tables);
        Assert.Equal("mserp_customer", result.Tables[0].LogicalName);
        Assert.Equal("https://dv.example/api/data/v9.2/EntityDefinitions?$skiptoken=Case", client.Requested[1]);
    }

    [Fact]
    public async Task Repeated_page_fails_without_partial_inventory_or_duplicate_dispatch()
    {
        var firstPath = $"https://dv.example/api/data/v9.2/EntityDefinitions?$select={VirtualTableMetadataParser.SelectColumns}";
        var first = $"{{\"value\":[{{\"LogicalName\":\"mserp_customer\",\"ExternalName\":\"Customer\"}}],\"@odata.nextLink\":{System.Text.Json.JsonSerializer.Serialize(firstPath)}}}";
        var client = new PagingClient(new ODataResponse(200, "OK", first, 1));

        var result = await new CoreVirtualTableReader(client, Env)
            .GetVirtualTablesAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Tables);
        Assert.Contains("incomplete", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(client.Requested);
    }

    [Fact]
    public async Task Later_http_or_malformed_page_discards_earlier_virtual_tables()
    {
        var first = "{\"value\":[{\"LogicalName\":\"mserp_customer\",\"ExternalName\":\"Customer\"}],\"@odata.nextLink\":\"next\"}";
        foreach (var later in new[]
        {
            new ODataResponse(500, "Server Error", "boom", 1),
            new ODataResponse(200, "OK", "{}", 1)
        })
        {
            var client = new PagingClient(new ODataResponse(200, "OK", first, 1), later);
            var result = await new CoreVirtualTableReader(client, Env)
                .GetVirtualTablesAsync(TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.Tables);
        }
    }
}
