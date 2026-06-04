using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteGatewayClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (DualWriteGatewayClient Client, CapturingHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new CapturingHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.example/") };
        return (new DualWriteGatewayClient(http), handler);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetEnvironment_BuildsExpectedRequest_AndParsesCidCname()
    {
        var (client, handler) = CreateClient(_ => Json("{\"cid\":\"C123\",\"cname\":\"contoso-link\"}"));

        var env = await client.GetEnvironmentAsync("uat-fo", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "https://gw.example/api/DualWriteManagement/1.0/Environments?targetType=AX&identifier=uat-fo",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("C123", env.Cid);
        Assert.Equal("contoso-link", env.Cname);
        Assert.Equal("uat-fo", env.Identifier);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetMaps_BuildsExpectedRequest_AndParsesMapsAndTemplates()
    {
        // Real gateway "Entities" shape (bare array): map fields under leftEntity/rightEntity/detail,
        // version as an object, state as a numeric MapStatus code.
        const string json = """
        [
          {
            "leftEntity": { "targetType": "AX", "name": "Customers V3", "displayName": "Customers V3" },
            "rightEntity": { "targetType": "CRM", "name": "accounts", "displayName": "accounts" },
            "detail": {
              "tid": "t-101",
              "tName": "accounts - Customers V3",
              "pid": "proj-1",
              "state": "4",
              "templates": [
                { "id": "t-100", "author": "Microsoft", "version": { "major": 1, "minor": 0, "build": 0, "revision": 0 } },
                { "id": "t-101", "author": "Contoso", "version": { "major": 1, "minor": 0, "build": 1, "revision": 0 } }
              ],
              "template": { "id": "t-101", "author": "Contoso", "version": { "major": 1, "minor": 0, "build": 1, "revision": 0 } }
            }
          }
        ]
        """;
        var (client, handler) = CreateClient(_ => Json(json));

        var maps = await client.GetMapsAsync("C123", CancellationToken.None);

        Assert.Equal(
            "https://gw.example/api/DualWriteManagement/1.0/Entities?targetType=AX&cid=C123",
            handler.LastRequest!.RequestUri!.ToString());
        var map = Assert.Single(maps);
        Assert.Equal("t-101", map.Id);
        Assert.Equal("Customers V3", map.DisplayName);
        Assert.Equal("accounts - Customers V3", map.Name);
        Assert.Equal("accounts", map.RightEntityName);
        Assert.Equal("proj-1", map.ProjectId);
        Assert.Equal("Running", map.State);
        Assert.Equal("1.0.1.0", map.CurrentVersion);
        Assert.Equal("Contoso", map.CurrentAuthor);
        Assert.Equal(2, map.Templates.Count);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task StartAction_PostsActionCodeAndDetails_AndParsesRequestId()
    {
        var (client, handler) = CreateClient(_ => Json("{\"requestId\":\"req-9\",\"state\":\"1\"}"));
        var map = new DualWriteMap(
            "map-1", "CustomersV3", "Customers", "proj-1", "Stopped",
            new DualWriteTemplate("t-101", "1.0.1", "Contoso"),
            Array.Empty<DualWriteTemplate>());

        var response = await client.StartActionAsync(DualWriteActionType.Start, new[] { map }, "C123", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://gw.example/api/DualWriteManagement/1.0/Start", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("req-9", response.RequestId);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("1", doc.RootElement.GetProperty("action").GetString());
        var detail = doc.RootElement.GetProperty("details")[0];
        Assert.Equal("t-101", detail.GetProperty("tid").GetString());
        Assert.Equal("C123", detail.GetProperty("cid").GetString());
        Assert.Equal("proj-1", detail.GetProperty("pid").GetString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetStatus_BuildsExpectedRequest_AndClassifiesTerminalSuccess()
    {
        var (client, handler) = CreateClient(_ => Json("{\"requestId\":\"req-9\",\"state\":\"2\"}"));

        var status = await client.GetStatusAsync("req-9", CancellationToken.None);

        Assert.Equal(
            "https://gw.example/api/DualWriteManagement/1.0/Status/req-9",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.True(status.IsTerminal);
        Assert.True(status.IsSuccess);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task NonSuccessStatus_ThrowsGatewayExceptionWithStatusCode()
    {
        var (client, _) = CreateClient(_ => Json("{\"error\":\"boom\"}", HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<DualWriteGatewayException>(
            () => client.GetMapsAsync("C123", CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("400", ex.Message);
    }

    [Trait("Category", "DualWrite")]
    [Theory]
    // Full URL (what the field holds today), bare host, and bare host with a trailing slash all
    // normalize to the same bare host the gateway's Environments lookup keys on.
    [InlineData("https://shl-uat.sandbox.operations.dynamics.com/", "shl-uat.sandbox.operations.dynamics.com")]
    [InlineData("https://shl-uat.sandbox.operations.dynamics.com", "shl-uat.sandbox.operations.dynamics.com")]
    [InlineData("shl-uat.sandbox.operations.dynamics.com", "shl-uat.sandbox.operations.dynamics.com")]
    [InlineData("shl-uat.sandbox.operations.dynamics.com/", "shl-uat.sandbox.operations.dynamics.com")]
    [InlineData("  shl-uat.sandbox.operations.dynamics.com  ", "shl-uat.sandbox.operations.dynamics.com")]
    public async Task GetEnvironment_NormalizesIdentifierToBareHost(string input, string expectedIdentifier)
    {
        // Arrange: a handler that returns one environment record and captures the request URI.
        var (client, handler) = CreateClient(_ => Json("[{\"cid\":\"c1\",\"cname\":\"n1\"}]"));

        // Act
        await client.GetEnvironmentAsync(input, CancellationToken.None);

        // Assert: identifier query parameter is the bare host — no scheme, no trailing slash.
        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains($"identifier={expectedIdentifier}", query);
        Assert.DoesNotContain("https", query);
        Assert.DoesNotContain($"identifier={expectedIdentifier}/", query);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetEnvironment_FallsBackToRawValue_WhenHostParsesEmpty()
    {
        // "host:port" with no scheme parses to an empty UriBuilder.Uri.Host; the normalizer must
        // fall back to the raw value rather than send the gateway an empty identifier.
        var (client, handler) = CreateClient(_ => Json("[{\"cid\":\"c1\",\"cname\":\"n1\"}]"));

        await client.GetEnvironmentAsync("shl-uat.example.com:8080", CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("identifier=shl-uat.example.com:8080", query);
    }
}

public class MapActionPayloadBuilderTests
{
    private static DualWriteMap Map(string id = "map-1", string pid = "proj-1", string tid = "t-101") =>
        new(id, "name", "display", pid, "Running", new DualWriteTemplate(tid, "1.0", "auth"), Array.Empty<DualWriteTemplate>());

    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData(DualWriteActionType.Start, "1")]
    [InlineData(DualWriteActionType.Stop, "4")]
    [InlineData(DualWriteActionType.Pause, "5")]
    [InlineData(DualWriteActionType.Resume, "6")]
    [InlineData(DualWriteActionType.InitialSync, "8")]
    public void Build_EmitsCorrectActionCode(DualWriteActionType action, string expected)
    {
        var json = MapActionPayloadBuilder.Build(action, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, doc.RootElement.GetProperty("action").GetString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_NonInitialSync_IncludesPid()
    {
        var json = MapActionPayloadBuilder.Build(DualWriteActionType.Stop, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        var detail = doc.RootElement.GetProperty("details")[0];
        Assert.True(detail.TryGetProperty("pid", out _));
        Assert.Equal("t-101", detail.GetProperty("tid").GetString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_InitialSync_OmitsPidAndParameters()
    {
        var json = MapActionPayloadBuilder.Build(DualWriteActionType.InitialSync, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        var detail = doc.RootElement.GetProperty("details")[0];
        Assert.False(detail.TryGetProperty("pid", out _));
        Assert.False(detail.TryGetProperty("parameters", out _));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_Start_IncludesParametersWithSkipInitialSyncAndConflictResolution()
    {
        var json = MapActionPayloadBuilder.Build(DualWriteActionType.Start, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        var parameters = doc.RootElement.GetProperty("details")[0].GetProperty("parameters");
        Assert.True(parameters.GetProperty("skipInitialSync").GetBoolean());
        var conflict = parameters.GetProperty("conflictResolution");
        Assert.Equal("1", conflict.GetProperty("option").GetString());
        Assert.Equal("CE", conflict.GetProperty("master").GetString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_Stop_SkipInitialSyncIsFalse()
    {
        var json = MapActionPayloadBuilder.Build(DualWriteActionType.Stop, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        var parameters = doc.RootElement.GetProperty("details")[0].GetProperty("parameters");
        Assert.False(parameters.GetProperty("skipInitialSync").GetBoolean());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_EmitsOneDetailPerMap()
    {
        var json = MapActionPayloadBuilder.Build(
            DualWriteActionType.Start,
            new[] { Map("a", tid: "ta"), Map("b", tid: "tb") },
            "C123");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("details").GetArrayLength());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_NoMaps_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MapActionPayloadBuilder.Build(DualWriteActionType.Start, Array.Empty<DualWriteMap>(), "C123"));
    }

    // Regression for #25: a selected map with an empty project id (pid) serialized to the gateway
    // triggers a server-side NullReferenceException (500). Reject it client-side with a clear,
    // map-named error instead of sending "pid":"".
    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData(DualWriteActionType.Start)]
    [InlineData(DualWriteActionType.Stop)]
    [InlineData(DualWriteActionType.Pause)]
    [InlineData(DualWriteActionType.Resume)]
    public void Build_NonInitialSyncWithEmptyProjectId_Throws(DualWriteActionType action)
    {
        var map = new DualWriteMap("id1", "Customers", "Customers V3 → accounts", "", "Stopped",
            new DualWriteTemplate("t1", "1.0", "MS"), Array.Empty<DualWriteTemplate>());

        var ex = Assert.Throws<ArgumentException>(() =>
            MapActionPayloadBuilder.Build(action, new[] { map }, "C123"));
        Assert.Contains("Customers V3", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_WithEmptyTemplateId_Throws()
    {
        // tid (template id) is required for every action; an empty tid also NREs the gateway.
        var map = new DualWriteMap("", "Vendors", "Vendors V2 → vendors", "proj-1", "Stopped",
            new DualWriteTemplate("", "1.0", "MS"), Array.Empty<DualWriteTemplate>());

        var ex = Assert.Throws<ArgumentException>(() =>
            MapActionPayloadBuilder.Build(DualWriteActionType.Start, new[] { map }, "C123"));
        Assert.Contains("Vendors V2", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("template", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Build_InitialSyncWithEmptyProjectId_DoesNotThrow()
    {
        // Initial sync omits pid entirely (action-8 shape), so a missing project id must not block it.
        var map = new DualWriteMap("id1", "Customers", "Customers", "", "Stopped",
            new DualWriteTemplate("t1", "1.0", "MS"), Array.Empty<DualWriteTemplate>());

        var json = MapActionPayloadBuilder.Build(DualWriteActionType.InitialSync, new[] { map }, "C123");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("details")[0].TryGetProperty("pid", out _));
    }
}

public class DualWriteStatusInterpreterTests
{
    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData("2", true, true)]
    [InlineData("success", true, true)]
    [InlineData("Completed", true, true)]
    [InlineData("3", true, false)]
    [InlineData("Failed", true, false)]
    [InlineData("Running", false, false)]
    [InlineData("1", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void Classify_MapsStatesToTerminalAndSuccess(string? state, bool terminal, bool success)
    {
        var (isTerminal, isSuccess) = DualWriteStatusInterpreter.Classify(state);
        Assert.Equal(terminal, isTerminal);
        Assert.Equal(success, isSuccess);
    }
}
