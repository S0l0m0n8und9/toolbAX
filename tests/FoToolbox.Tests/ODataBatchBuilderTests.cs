using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Xunit;

namespace FoToolbox.Tests;

public class ODataBatchBuilderTests
{
    [Fact]
    public void BuildWriteBatch_BuildsMultipartWithChangeset()
    {
        var ops = new[]
        {
            new ODataBatchOperation(HttpMethod.Post, "https://contoso.operations.dynamics.com/data/CustomersV3", JsonBody: "{\"CustomerAccount\":\"C0001\"}"),
            new ODataBatchOperation(new HttpMethod("PATCH"), "/data/CustomersV3(CustomerAccount='C0001')", JsonBody: "{\"Name\":\"Contoso\"}",
                Headers: new[] { new KeyValuePair<string,string>("If-Match","*") }.ToDictionary(k => k.Key, v => v.Value))
        };

        var built = ODataBatchBuilder.BuildWriteBatch("https://contoso.operations.dynamics.com", ops);

        Assert.EndsWith("/data/$batch", built.BatchUrl);
        Assert.Contains("multipart/mixed; boundary=batch_", built.ContentType);

        var boundary = built.ContentType.Split("boundary=", StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        Assert.StartsWith("batch_", boundary);
        Assert.Contains($"--{boundary}", built.Body);
        Assert.Contains($"--{boundary}--", built.Body);

        Assert.Contains("Content-Type: multipart/mixed; boundary=changeset_", built.Body);

        // Two operations => two content ids.
        Assert.Contains("Content-ID: 1", built.Body);
        Assert.Contains("Content-ID: 2", built.Body);

        // Ensure each inner request is application/http and includes Accept.
        Assert.Contains("Content-Type: application/http", built.Body);
        Assert.Contains("Accept: application/json", built.Body);

        // Method lines use path-and-query.
        Assert.Contains("POST /data/CustomersV3 HTTP/1.1", built.Body);
        Assert.Contains("PATCH /data/CustomersV3(CustomerAccount='C0001') HTTP/1.1", built.Body);

        // JSON bodies are present.
        Assert.Contains("Content-Type: application/json", built.Body);
        Assert.Contains("{\"CustomerAccount\":\"C0001\"}", built.Body);
        Assert.Contains("{\"Name\":\"Contoso\"}", built.Body);

        // Header passthrough.
        Assert.Contains("If-Match: *", built.Body);
    }

    [Fact]
    public void BuildWriteBatch_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => ODataBatchBuilder.BuildWriteBatch("https://contoso.operations.dynamics.com", Array.Empty<ODataBatchOperation>()));
    }

    [Fact]
    public void BuildWriteBatch_Terminates_Every_Line_With_Crlf_Bytes()
    {
        // MIME multipart is CRLF-only (RFC 2046 5.1.1). Asserted on the bytes because a substring
        // assertion passes on Windows regardless of what the builder emits elsewhere.
        var built = ODataBatchBuilder.BuildWriteBatch(
            "https://contoso.operations.dynamics.com",
            new[]
            {
                new ODataBatchOperation(HttpMethod.Post, "/data/CustomersV3", JsonBody: "{\"CustomerAccount\":\"C0001\"}"),
                new ODataBatchOperation(HttpMethod.Delete, "/data/CustomersV3(CustomerAccount='C0002')")
            });

        var bytes = Encoding.UTF8.GetBytes(built.Body);
        const byte Cr = (byte)'\r';
        const byte Lf = (byte)'\n';

        var crlfCount = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != Lf)
            {
                continue;
            }

            Assert.True(i > 0 && bytes[i - 1] == Cr, $"Bare LF (no preceding CR) at byte offset {i}.");
            crlfCount++;
        }

        // A lone CR (old-Mac style) is just as broken as a lone LF.
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == Cr)
            {
                Assert.True(i + 1 < bytes.Length && bytes[i + 1] == Lf, $"Bare CR (no following LF) at byte offset {i}.");
            }
        }

        Assert.True(crlfCount > 10, $"Expected the batch body to be built from CRLF-terminated lines, saw {crlfCount}.");

        // The structural separators specifically must be CRLF-terminated.
        var boundary = built.ContentType.Split("boundary=", StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        Assert.Contains($"--{boundary}\r\nContent-Type: multipart/mixed; boundary=changeset_", built.Body);
        Assert.EndsWith($"--{boundary}--\r\n", built.Body);
        Assert.Contains("Content-Transfer-Encoding: binary\r\n", built.Body);
    }

    [Fact]
    public void BuildWriteBatch_Rejects_An_Operation_Url_On_A_Foreign_Origin()
    {
        // A batch executes wholly against BatchUrl's environment under that environment's token, so an
        // op URL naming another environment must not be silently reduced to its path and executed here.
        var ops = new[]
        {
            new ODataBatchOperation(HttpMethod.Post, "https://evil.example.com/data/CustomersV3", JsonBody: "{}")
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            ODataBatchBuilder.BuildWriteBatch("https://contoso.operations.dynamics.com", ops));

        Assert.Contains("https://evil.example.com", ex.Message);
        Assert.Contains("https://contoso.operations.dynamics.com", ex.Message);
    }

    [Theory]
    [InlineData("http://contoso.operations.dynamics.com/data/CustomersV3")] // scheme downgrade
    [InlineData("https://contoso.operations.dynamics.com:8443/data/CustomersV3")] // alternate port
    [InlineData("//evil.example.com/data/CustomersV3")] // RFC 3986 network-path reference
    public void BuildWriteBatch_Rejects_Operation_Urls_That_Change_The_Origin(string opUrl)
    {
        var ops = new[] { new ODataBatchOperation(HttpMethod.Post, opUrl, JsonBody: "{}") };

        Assert.Throws<ArgumentException>(() =>
            ODataBatchBuilder.BuildWriteBatch("https://contoso.operations.dynamics.com", ops));
    }

    [Fact]
    public void BuildWriteBatch_Accepts_An_Absolute_Operation_Url_On_The_Same_Origin()
    {
        var ops = new[]
        {
            new ODataBatchOperation(HttpMethod.Post, "https://contoso.operations.dynamics.com:8443/data/CustomersV3?cross-company=true", JsonBody: "{}")
        };

        var built = ODataBatchBuilder.BuildWriteBatch("https://contoso.operations.dynamics.com:8443/", ops);

        Assert.Contains("POST /data/CustomersV3?cross-company=true HTTP/1.1\r\n", built.Body);
    }
}
