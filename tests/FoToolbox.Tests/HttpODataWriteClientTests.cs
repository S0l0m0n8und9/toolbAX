using FoToolbox.Core.OData;
using System;
using System.Net.Http;
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
}

