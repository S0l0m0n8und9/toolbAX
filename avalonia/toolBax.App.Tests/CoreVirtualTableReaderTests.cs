using System.Threading;
using System.Threading.Tasks;
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
}
