using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreDualWriteMapReader"/> over a fake <see cref="IDataverseClient"/>: the maps
/// query path, server-driven paging (nextLink), accumulation, and error surfacing. No network.
/// </summary>
public class CoreDualWriteMapReaderTests
{
    private sealed class FakeDataverseClient : IDataverseClient
    {
        private readonly Queue<ODataResponse> _responses;
        public List<string> Requested { get; } = new();

        public FakeDataverseClient(params ODataResponse[] responses) => _responses = new Queue<ODataResponse>(responses);

        public Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Requested.Add(pathOrUrl);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static ODataResponse Ok(string body) => new(200, "OK", body, 1);

    private const string OneMap = """
    { "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha", "msdyn_displayname": "Alpha" } ] }
    """;

    [Fact]
    public async Task GetMaps_queries_the_dualwrite_map_entity_set()
    {
        var dv = new FakeDataverseClient(Ok(OneMap));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Maps);
        Assert.Equal("Alpha", result.Maps[0].DisplayName);
        Assert.StartsWith("msdyn_dualwriteentitymaps?", dv.Requested[0]);
    }

    [Fact]
    public async Task GetMaps_follows_next_link_and_accumulates_pages()
    {
        const string page1 = """
        { "@odata.nextLink": "https://x/api/data/v9.2/page2", "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha" } ] }
        """;
        const string page2 = """
        { "value": [ { "msdyn_dualwriteentitymapid": "b", "msdyn_name": "beta" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(page1), Ok(page2));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Maps.Count);
        Assert.Equal("https://x/api/data/v9.2/page2", dv.Requested[1]); // the nextLink, used verbatim
    }

    [Fact]
    public async Task A_non_success_response_surfaces_as_an_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(401, "Unauthorized", "token denied", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Maps);
        Assert.Contains("Unauthorized", result.Error);
    }

    [Fact]
    public async Task A_failure_on_a_later_page_surfaces_as_an_error()
    {
        const string page1 = """
        { "@odata.nextLink": "https://x/api/data/v9.2/page2", "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(page1), new ODataResponse(500, "Server Error", "boom", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Server Error", result.Error);
    }
}
