using DualWriteMapBrowserPlugin;
using Xunit;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class TestifyValueMapParserTests
{
    [Fact]
    public void TryExtractKeys_ObjectValueMap_UsesObjectKeys()
    {
        const string json = "{\"A\":\"x\",\"B\":\"y\"}";

        var ok = TestifyValueMapParser.TryExtractKeys(json, out var keys, out var error);

        Assert.True(ok, error);
        Assert.Contains("A", keys);
        Assert.Contains("B", keys);
    }

    [Fact]
    public void TryExtractKeys_ArrayValueMap_UsesSourceCandidates()
    {
        const string json = "[{\"source\":\"Retail\",\"target\":\"1\"},{\"from\":\"Wholesale\",\"target\":\"2\"}]";

        var ok = TestifyValueMapParser.TryExtractKeys(json, out var keys, out var error);

        Assert.True(ok, error);
        Assert.Contains("Retail", keys);
        Assert.Contains("Wholesale", keys);
    }

    [Fact]
    public void TryExtractKeys_InvalidJson_ReturnsFalse()
    {
        var ok = TestifyValueMapParser.TryExtractKeys("{not-json", out var keys, out var error);

        Assert.False(ok);
        Assert.Empty(keys);
        Assert.Contains("Invalid valueMap JSON", error);
    }

    [Fact]
    public void TryExtractMappings_ObjectValueMap_ExtractsTargets()
    {
        const string json = "{\"Open\":\"1\",\"Closed\":\"2\"}";

        var ok = TestifyValueMapParser.TryExtractMappings(json, out var mappings, out var error);

        Assert.True(ok, error);
        Assert.Equal("1", mappings["Open"]);
        Assert.Equal("2", mappings["Closed"]);
    }

    [Fact]
    public void TryExtractMappings_ArrayValueMap_ExtractsSourceAndTarget()
    {
        const string json = "[{\"source\":\"Retail\",\"target\":\"100\"},{\"from\":\"Wholesale\",\"to\":\"200\"}]";

        var ok = TestifyValueMapParser.TryExtractMappings(json, out var mappings, out var error);

        Assert.True(ok, error);
        Assert.Equal("100", mappings["Retail"]);
        Assert.Equal("200", mappings["Wholesale"]);
    }
}
