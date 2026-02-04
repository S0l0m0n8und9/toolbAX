using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class FakeODataServerTests
{
    [Fact]
    public async Task Serves_Metadata_And_Paged_Results()
    {
        var metadataPath = Path.Combine(AppContext.BaseDirectory, "Resources", "SampleMetadata.xml");
        var metadataXml = await File.ReadAllTextAsync(metadataPath);

        await using var server = FakeODataServer.Create(metadataXml);

        var cachePath = Path.Combine(Path.GetTempPath(), $"metadata-{Guid.NewGuid():N}.db");
        var cache = new ODataMetadataCache($"Data Source={cachePath}");
        var provider = new ODataMetadataProvider(server.Client, cache);

        var metadata = await provider.GetMetadataAsync("env", server.BaseUri.ToString().TrimEnd('/'));
        Assert.NotEmpty(metadata.Entities);

        var odata = new HttpODataClient(server.Client);
        var pages = new List<ODataPage>();
        await foreach (var page in odata.StreamAsync(new QueryRequest("/data/Customers")))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal(1L, Convert.ToInt64(pages[0].Rows[0]["Id"]));
        Assert.Equal(2L, Convert.ToInt64(pages[1].Rows[0]["Id"]));
    }
}
