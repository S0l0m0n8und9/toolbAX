using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreDualWriteMapReader"/> over a fake <see cref="IDataverseClient"/>: the maps +
/// solutions query paths, server-driven paging (nextLink), accumulation, solution filtering, and error
/// surfacing. No network.
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

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

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

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Maps.Count);
        Assert.Equal("https://x/api/data/v9.2/page2", dv.Requested[1]); // the nextLink, used verbatim
    }

    [Fact]
    public async Task A_non_success_response_surfaces_as_an_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(401, "Unauthorized", "token denied", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

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

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Server Error", result.Error);
    }

    [Fact]
    public async Task GetSolutions_queries_the_solutions_entity_set()
    {
        const string solutions = """
        { "value": [ { "solutionid": "55555555-5555-5555-5555-555555555555", "uniquename": "cust", "friendlyname": "Cust", "publisherid": { "uniquename": "p", "friendlyname": "Pub" } } ] }
        """;
        var dv = new FakeDataverseClient(Ok(solutions));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetSolutionsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Solutions);
        Assert.Equal("cust", result.Solutions[0].UniqueName);
        Assert.StartsWith("solutions?", dv.Requested[0]);
    }

    [Fact]
    public async Task GetMaps_for_a_solution_filters_to_its_components()
    {
        // First request = solution components (object ids); second = all maps; result keeps only matches.
        const string components = """
        { "value": [ { "objectid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" } ] }
        """;
        const string maps = """
        { "value": [
            { "msdyn_dualwriteentitymapid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "msdyn_name": "in-solution" },
            { "msdyn_dualwriteentitymapid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "msdyn_name": "not-in-solution" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(components), Ok(maps));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Maps);
        Assert.Equal("in-solution", result.Maps[0].Name);
        Assert.StartsWith("solutioncomponents?", dv.Requested[0]); // components fetched first
    }

    [Fact]
    public async Task GetMaps_for_a_solution_surfaces_a_component_fetch_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(403, "Forbidden", "no access", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Forbidden", result.Error);
    }
}
