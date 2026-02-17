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
        Assert.Equal("20", account.MaxLength);

        var name = entity.Properties.First(p => p.Name == "Name");
        Assert.False(name.IsKey);
        Assert.False(name.Mandatory);
        Assert.Equal("100", name.MaxLength);
    }

    [Fact]
    public async Task TryParseEntityDetails_Parses_Facets_And_Range_Annotations()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine("Resources", "SampleMetadata.xml"));
        var entity = ODataMetadataIndexParser.TryParseEntityDetails(xml, "SalesOrdersV3");

        Assert.NotNull(entity);

        var totalAmount = entity!.Properties.First(p => p.Name == "TotalAmount");
        Assert.Equal("32", totalAmount.Precision);
        Assert.Equal("6", totalAmount.Scale);
        Assert.Equal("0", totalAmount.MinValue);
        Assert.Equal("999999999999.999999", totalAmount.MaxValue);

        var quantity = entity.Properties.First(p => p.Name == "Quantity");
        Assert.Equal("1", quantity.MinValue);
        Assert.Equal("1000", quantity.MaxValue);
    }

    [Fact]
    public void TryParseEntityDetails_Parses_Aliased_Annotation_Facets_And_Default_Int32_Range()
    {
        var xml = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Container">
        <EntitySet Name="AliasEntities" EntityType="Default.AliasEntity" />
      </EntityContainer>
      <EntityType Name="AliasEntity">
        <Property Name="Code" Type="Edm.String">
          <Annotation Term="Validation.MaxLength" Int="12" />
        </Property>
        <Property Name="Level" Type="Edm.Int32" />
        <Property Name="Threshold" Type="Edm.Int32">
          <Annotation Term="Validation.Minimum" Int="5" />
        </Property>
      </EntityType>
    </Schema>
    <Schema Namespace="Org.OData.Validation.V1" Alias="Validation" xmlns="http://docs.oasis-open.org/odata/ns/edm" />
  </edmx:DataServices>
</edmx:Edmx>
""";

        var entity = ODataMetadataIndexParser.TryParseEntityDetails(xml, "AliasEntities");

        Assert.NotNull(entity);

        var code = entity!.Properties.First(p => p.Name == "Code");
        Assert.Equal("12", code.MaxLength);

        var level = entity.Properties.First(p => p.Name == "Level");
        Assert.Equal("-2147483648", level.MinValue);
        Assert.Equal("2147483647", level.MaxValue);

        var threshold = entity.Properties.First(p => p.Name == "Threshold");
        Assert.Equal("5", threshold.MinValue);
        Assert.Equal("2147483647", threshold.MaxValue);
    }
}
