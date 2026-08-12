using FoToolbox.Core.OData;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
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

        // A multipart Content-Type carries a boundary and no charset (the parts declare their own
        // encodings), so charset normalisation must leave the batch header exactly as built — asserted
        // both ways: nothing added, and byte-identical to built.ContentType above.
        Assert.DoesNotContain("charset", built.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charset", handler.RequestContentType, StringComparison.OrdinalIgnoreCase);
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
        // Guard: an absent charset is filled in from the encoding actually used.
        Assert.Equal("application/json; charset=utf-8", handler.RequestContentType);
    }

    [Fact]
    public async Task SendAsync_Rewrites_A_NonUtf8_Declared_Charset_To_Match_The_Bytes_Sent()
    {
        // The body is always encoded UTF-8, so honouring a caller's "iso-8859-1" would ship a header that
        // lies about the payload. (Re-encoding instead is worse: most legacy codepages are unavailable on
        // .NET Core without registering CodePagesEncodingProvider.)
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new HttpODataWriteClient(new HttpClient(handler));
        const string body = "{\"Name\":\"Ærø Ünïcode – ✓\"}";

        await client.SendAsync(new ODataWriteRequest(
            HttpMethod.Post,
            "https://contoso.operations.dynamics.com/data/CustomersV3",
            Body: body,
            ContentType: "application/json; charset=iso-8859-1"));

        Assert.Equal("application/json; charset=utf-8", handler.RequestContentType);

        // ...and the declaration is now true: the bytes are valid UTF-8 that decodes back to the body.
        Assert.NotNull(handler.RequestBytes);
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        Assert.Equal(body, strictUtf8.GetString(handler.RequestBytes!));
        Assert.Equal(strictUtf8.GetByteCount(body), handler.RequestBytes!.Length);
    }

    [Fact]
    public async Task SendAsync_Leaves_A_Declared_Utf8_Charset_Alone()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new HttpODataWriteClient(new HttpClient(handler));

        await client.SendAsync(new ODataWriteRequest(
            HttpMethod.Post,
            "https://contoso.operations.dynamics.com/data/CustomersV3",
            Body: "{\"Name\":\"Contoso\"}",
            ContentType: "application/json; charset=UTF-8"));

        // Case-insensitive match, so a truthful declaration is left as the caller wrote it.
        Assert.Equal("application/json; charset=UTF-8", handler.RequestContentType);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public string? RequestContentType { get; private set; }
        public string? RequestBody { get; private set; }

        /// <summary>The bytes actually put on the wire, so a test can check them against the declaration.</summary>
        public byte[]? RequestBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestContentType = request.Content?.Headers.ContentType?.ToString();
            if (request.Content is not null)
            {
                RequestBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_status) { Content = new StringContent("{}") };
        }
    }
}

