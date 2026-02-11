using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
}
