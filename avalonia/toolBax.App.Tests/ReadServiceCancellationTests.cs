using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class ReadServiceCancellationTests
{
    private static EnvProfile Env() => new("a", "A", "https://fo.example", "tenant", "USMF", "Tier 1", EnvStatus.Connected, DataverseUrl: "https://dv.example");

    private sealed class Auth : IAuthService
    {
        public int Calls { get; private set; }
        public Func<Task<string>> Acquire { get; set; } = () => Task.FromResult("token");
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            Calls++;
            return Acquire();
        }
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return send(ct);
        }
    }

    private sealed class Dataverse : IDataverseClient
    {
        public List<string> Calls { get; } = new();
        public Func<string, Task<ODataResponse>> Read { get; set; } = _ => Task.FromResult(Json("{\"value\":[]}"));
        public Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Calls.Add(pathOrUrl);
            return Read(pathOrUrl);
        }
    }

    private static ODataResponse Json(string body) => new(200, "OK", body, 0);

    [Fact]
    public async Task Dataverse_precancelled_request_does_not_auth_dispatch_or_trace_failure()
    {
        var auth = new Auth();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        using var client = new CoreDataverseClient(auth, Env, http);
        using var cts = new CancellationTokenSource();
        using var trace = new TraceCapture();
        var path = "h10a-" + Guid.NewGuid().ToString("N");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(path, cts.Token));
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain(path, trace.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dataverse_cancelled_auth_late_success_or_fault_is_cancellation(bool fault)
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auth = new Auth { Acquire = () => gate.Task };
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        using var client = new CoreDataverseClient(auth, Env, http);
        using var cts = new CancellationTokenSource();
        var pending = client.GetAsync("accounts", cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late auth fault"));
        else gate.SetResult("late token");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dataverse_cancelled_http_late_success_or_fault_cannot_publish(bool fault)
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ => gate.Task);
        using var http = new HttpClient(handler);
        using var client = new CoreDataverseClient(new Auth(), Env, http);
        using var cts = new CancellationTokenSource();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}") };
        await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        var pending = client.GetAsync("accounts", cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late HTTP fault"));
        else gate.SetResult(response);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dataverse_real_http_timeout_with_live_caller_token_exhausts_bounded_retries_as_failure()
    {
        using var handler = new Handler(async ct =>
        {
            await new TaskCompletionSource().Task.WaitAsync(ct);
            throw new InvalidOperationException("unreachable");
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var client = new CoreDataverseClient(new Auth(), Env, http);
        var response = await client.GetAsync("accounts", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(response.IsSuccess);
        Assert.Equal(0, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    private static Task ReadOperation(string kind, Dataverse source, CancellationToken ct)
    {
        var reader = new CoreDualWriteMapReader(source, Env);
        return kind switch
        {
            "maps" => reader.GetMapsAsync(ct: ct),
            "solutions" => reader.GetSolutionsAsync(ct),
            "components" => reader.GetMapsAsync("solution", ct),
            "count" => reader.GetCeRowCountAsync("accounts", null, ct),
            _ => new CoreVirtualTableReader(source).GetVirtualTablesAsync(ct)
        };
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("solutions")]
    [InlineData("components")]
    [InlineData("count")]
    [InlineData("virtual")]
    public async Task Precancellation_stops_readers_before_the_first_dependency_call(string kind)
    {
        var source = new Dataverse();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOperation(kind, source, cts.Token));
        Assert.Empty(source.Calls);
    }

    [Theory]
    [InlineData("maps", false)]
    [InlineData("maps", true)]
    [InlineData("solutions", false)]
    [InlineData("solutions", true)]
    [InlineData("components", false)]
    [InlineData("components", true)]
    [InlineData("count", false)]
    [InlineData("count", true)]
    [InlineData("virtual", false)]
    [InlineData("virtual", true)]
    public async Task Cancelled_readers_discard_ignoring_success_or_other_exception(string kind, bool fault)
    {
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Dataverse();
        source.Read = _ => source.Calls.Count == 1 ? gate.Task : Task.FromResult(Json("{\"value\":[]}"));
        using var cts = new CancellationTokenSource();
        var pending = ReadOperation(kind, source, cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late read fault"));
        else gate.SetResult(Json(kind == "count" ? "{\"@odata.count\":5000}" : "{\"value\":[],\"@odata.nextLink\":\"https://dv.example/api/data/v9.2/next\"}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Single(source.Calls);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Cancelled_count_phase_stops_followons_and_preserves_logical_name_cache_rules(int phase)
    {
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Dataverse();
        source.Read = path =>
        {
            if (source.Calls.Count == phase) { entered.TrySetResult(); return gate.Task; }
            return Task.FromResult(Json(path.Contains("RetrieveTotalRecordCount", StringComparison.Ordinal)
                ? "{\"EntityRecordCountCollection\":{\"account\":6000}}"
                : path.Contains("EntityDefinitions", StringComparison.Ordinal)
                    ? "{\"value\":[{\"LogicalName\":\"account\"}]}" : "{\"@odata.count\":5000}"));
        };
        var reader = new CoreDualWriteMapReader(source, Env);
        var pending = reader.GetCeRowCountAsync("accounts", null, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        gate.SetResult(phase == 2 ? Json("{\"value\":[{\"LogicalName\":\"account\"}]}") : new ODataResponse(500, "failure", "", 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(phase, source.Calls.Count);
        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        Assert.Equal(phase == 2 ? 2 : 1, source.Calls.Count(p => p.Contains("EntityDefinitions", StringComparison.Ordinal)));
    }

    private sealed class Catalog : ICatalogService
    {
        public int Calls { get; private set; }
        public Func<Task<ODataMetadata>> Read { get; set; } = () => Task.FromResult(Metadata("Old"));
        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        { Calls++; return Read(); }
        public Task<TableCatalog> GetTablesAsync(FoEnvironment e, CatalogRefreshMode m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment e, CatalogRefreshMode m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshAsync(FoEnvironment e, CatalogRefreshScope s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment e, string j, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetTableBrowserUrlTemplateAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public string BuildTableBrowserUrl(FoEnvironment e, string t) => throw new NotSupportedException();
        public string BuildODataEntityUrl(FoEnvironment e, string n) => throw new NotSupportedException();
    }

    private static ODataMetadata Metadata(string name) => new(new[]
    {
        new ODataEntity(name + "Entity", Array.Empty<ODataProperty>(), Array.Empty<ODataNavigationProperty>()),
        new ODataEntity("Records", new[] { new ODataProperty(name + "Field", "Edm.String", true) },
            new[] { new ODataNavigationProperty(name + "Link", "Ns.Entity") })
    }, new[] { new ODataEnumType("Ns.Mode", new[] { name }) }, "same-etag");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancelled_metadata_preserves_previous_entities_fields_navigation_and_enums(bool fields, bool fault)
    {
        var catalog = new Catalog();
        var service = new CoreMetadataService(catalog, Env);
        await service.LoadEntitiesAsync(TestContext.Current.CancellationToken);
        await service.LoadFieldsAsync("Records", TestContext.Current.CancellationToken);
        var gate = new TaskCompletionSource<ODataMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.Read = () => gate.Task;
        using var cts = new CancellationTokenSource();
        var pending = fields ? (Task)service.LoadFieldsAsync("Records", true, cts.Token) : service.LoadEntitiesAsync(true, cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late catalog fault"));
        else gate.SetResult(Metadata("New"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Contains(service.GetEntities(), entity => entity.Name == "OldEntity");
        Assert.Equal("OldField", Assert.Single(service.GetFields("Records")!).Name);
        Assert.Equal("OldLink", Assert.Single(service.GetNavigations("Records")!));
        Assert.Equal("Old", Assert.Single(service.GetEnumMembers("Mode")!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Precanceled_metadata_does_not_clear_previous_scope_or_call_catalog(bool fields)
    {
        var current = Env();
        var catalog = new Catalog();
        var service = new CoreMetadataService(catalog, () => current);
        await service.LoadEntitiesAsync(TestContext.Current.CancellationToken);
        await service.LoadFieldsAsync("Records", TestContext.Current.CancellationToken);
        current = current with { Url = "https://other.example" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fields ? service.LoadFieldsAsync("Records", true, cts.Token) : service.LoadEntitiesAsync(true, cts.Token));
        Assert.Equal(2, catalog.Calls);
        current = Env();
        Assert.Contains(service.GetEntities(), entity => entity.Name == "OldEntity");
        Assert.Equal("OldField", Assert.Single(service.GetFields("Records")!).Name);
    }

    [Theory]
    [InlineData("maps", false)]
    [InlineData("maps", true)]
    [InlineData("solutions", false)]
    [InlineData("solutions", true)]
    [InlineData("components", false)]
    [InlineData("components", true)]
    public async Task Cancellation_on_later_page_never_returns_accumulated_partial_success(string kind, bool fault)
    {
        var first = kind switch
        {
            "maps" => "{\"msdyn_dualwriteentitymapid\":\"11111111-1111-1111-1111-111111111111\",\"msdyn_name\":\"First\"}",
            "solutions" => "{\"solutionid\":\"11111111-1111-1111-1111-111111111111\",\"uniquename\":\"First\"}",
            _ => "{\"objectid\":\"11111111-1111-1111-1111-111111111111\"}"
        };
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Dataverse();
        source.Read = _ =>
        {
            if (source.Calls.Count == 1)
                return Task.FromResult(Json("{\"value\":[" + first + "],\"@odata.nextLink\":\"https://dv.example/api/data/v9.2/next\"}"));
            if (source.Calls.Count == 2) { entered.TrySetResult(); return gate.Task; }
            return Task.FromResult(Json("{\"value\":[]}"));
        };
        using var cts = new CancellationTokenSource();
        var pending = ReadOperation(kind, source, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late page fault"));
        else gate.SetResult(Json("{malformed"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(2, source.Calls.Count);
    }

    [Fact]
    public async Task Dataverse_auth_timeout_with_live_caller_keeps_auth_failure()
    {
        var auth = new Auth { Acquire = () => Task.FromException<string>(new TaskCanceledException("auth timeout")) };
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        using var client = new CoreDataverseClient(auth, Env, http);
        var response = await client.GetAsync("accounts", TestContext.Current.CancellationToken);
        Assert.Equal(401, response.StatusCode);
        Assert.Contains("auth timeout", response.Body);
        Assert.Equal(0, handler.Calls);
    }
}
