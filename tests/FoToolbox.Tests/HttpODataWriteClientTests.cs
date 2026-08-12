using FoToolbox.Core.OData;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class HttpODataWriteClientTests
{
    [Fact]
    public async Task SendAsync_Posts_Json_And_Returns_Response()
    {
        await using var server = FakeODataServer.Create(metadataXml: string.Empty);
        var client = new HttpODataWriteClient(server.Client);

        var url = new Uri(server.BaseUri, "data/TestEntity").ToString();
        var json = "{\"foo\":\"bar\"}";

        var resp = await client.SendAsync(new ODataWriteRequest(HttpMethod.Post, url, json));

        Assert.Equal(201, resp.StatusCode);
        Assert.Equal(json, resp.Body);
        Assert.True(resp.Headers.TryGetValue("X-Echo-Method", out var method));
        Assert.Equal("POST", method);
    }

    [Fact]
    public async Task SendAsync_Sends_A_Batch_Body_With_Its_Parameterised_ContentType()
    {
        // The two halves of the write path have to compose: whatever ODataBatchBuilder produces must be
        // sendable as-is. Its ContentType carries a boundary parameter, which the StringContent
        // "mediaType" argument rejects outright (FormatException) rather than sending.
        var built = ODataBatchBuilder.BuildWriteBatch(
            "https://contoso.operations.dynamics.com",
            new[]
            {
                new ODataBatchOperation(HttpMethod.Post, "/data/CustomersV3", JsonBody: "{\"CustomerAccount\":\"C0001\"}")
            });

        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var client = new HttpODataWriteClient(new HttpClient(handler));

        var resp = await client.SendAsync(new ODataWriteRequest(
            HttpMethod.Post,
            built.BatchUrl,
            Body: built.Body,
            ContentType: built.ContentType));

        Assert.Equal(202, resp.StatusCode);
        Assert.Equal(built.ContentType, handler.RequestContentType);
        Assert.StartsWith("multipart/mixed; boundary=batch_", handler.RequestContentType);
        Assert.Equal(built.Body, handler.RequestBody);
    }

    [Fact]
    public async Task SendAsync_Defaults_A_Body_Without_ContentType_To_Json()
    {
        // application/octet-stream is a guaranteed 415 against an OData endpoint; JSON is the only
        // sensible default for this client.
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new HttpODataWriteClient(new HttpClient(handler));

        await client.SendAsync(new ODataWriteRequest(
            new HttpMethod("PATCH"),
            "https://contoso.operations.dynamics.com/data/CustomersV3",
            Body: "{\"Name\":\"Contoso\"}"));

        Assert.NotNull(handler.RequestContentType);
        Assert.StartsWith("application/json", handler.RequestContentType);
        Assert.DoesNotContain("octet-stream", handler.RequestContentType);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public string? RequestContentType { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestContentType = request.Content?.Headers.ContentType?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status) { Content = new StringContent("{}") };
        }
    }
}

