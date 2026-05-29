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
        const string json = """
        {
          "value": [
            {
              "id": "map-1",
              "name": "CustomersV3",
              "displayName": "Customers",
              "state": "Running",
              "detail": {
                "pid": "proj-1",
                "templates": [
                  { "id": "t-100", "version": "1.0.0", "author": "Microsoft" },
                  { "id": "t-101", "version": "1.0.1", "author": "Contoso" }
                ]
              },
              "template": { "id": "t-101", "version": "1.0.1", "author": "Contoso" }
            }
          ]
        }
        """;
        var (client, handler) = CreateClient(_ => Json(json));

        var maps = await client.GetMapsAsync("C123", CancellationToken.None);

        Assert.Equal(
            "https://gw.example/api/DualWriteManagement/1.0/Entities?targetType=AX&cid=C123",
            handler.LastRequest!.RequestUri!.ToString());
        var map = Assert.Single(maps);
        Assert.Equal("map-1", map.Id);
        Assert.Equal("Customers", map.DisplayName);
        Assert.Equal("proj-1", map.ProjectId);
        Assert.Equal("Running", map.State);
        Assert.Equal("1.0.1", map.CurrentVersion);
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
    public void Build_InitialSync_OmitsPid()
    {
        var json = MapActionPayloadBuilder.Build(DualWriteActionType.InitialSync, new[] { Map() }, "C123");
        using var doc = JsonDocument.Parse(json);
        var detail = doc.RootElement.GetProperty("details")[0];
        Assert.False(detail.TryGetProperty("pid", out _));
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
