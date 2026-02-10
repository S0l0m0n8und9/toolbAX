using FoToolbox.Core.OData;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ODataMetadataIndexParserTests
{
    [Fact]
    public async Task TryParseEntityDetails_Includes_Key_And_Required_Metadata()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine("Resources", "SampleMetadata.xml"));
        var entity = ODataMetadataIndexParser.TryParseEntityDetails(xml, "CustomersV3");

        Assert.NotNull(entity);

        var account = entity!.Properties.First(p => p.Name == "AccountNumber");
        Assert.True(account.IsKey);
        Assert.True(account.Mandatory);

        var name = entity.Properties.First(p => p.Name == "Name");
        Assert.False(name.IsKey);
        Assert.False(name.Mandatory);
    }
}
