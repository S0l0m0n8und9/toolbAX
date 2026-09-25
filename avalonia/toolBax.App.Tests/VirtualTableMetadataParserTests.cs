using System.Linq;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class VirtualTableMetadataParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"value\":{}}")]
    [InlineData("{\"value\":[{},false]}")]
    public void Malformed_collection_is_not_a_valid_empty_result(string json) =>
        Assert.Throws<MetadataResponseFormatException>(() => VirtualTableMetadataParser.Parse(json));

    [Fact]
    public void Valid_empty_collection_is_success() =>
        Assert.Empty(VirtualTableMetadataParser.Parse("{\"value\":[]}"));
    private const string Sample = """
    {"value":[
      {"LogicalName":"mserp_custcustomerv3entity","DisplayName":{"UserLocalizedLabel":{"Label":"Customer V3"}},"ExternalName":"CustCustomerV3Entity","ExternalCollectionName":"mserp_custcustomerv3entities","DataProviderId":"11111111-1111-1111-1111-111111111111","DataSourceId":"22222222-2222-2222-2222-222222222222","IsManaged":true},
      {"LogicalName":"contoso_sqlproduct","DisplayName":{"UserLocalizedLabel":{"Label":"SQL Product"}},"ExternalName":"Products","DataProviderId":"33333333-3333-3333-3333-333333333333","IsManaged":false},
      {"LogicalName":"account","DisplayName":{"UserLocalizedLabel":{"Label":"Account"}},"DataProviderId":"00000000-0000-0000-0000-000000000000","IsManaged":true}
    ]}
    """;

    [Fact]
    public void Keeps_only_virtual_tables_excluding_physical_ones()
    {
        var tables = VirtualTableMetadataParser.Parse(Sample);

        // account is physical (empty-guid provider, no external name) and is excluded.
        Assert.Equal(2, tables.Count);
        Assert.DoesNotContain(tables, t => t.LogicalName == "account");
    }

    [Fact]
    public void Classifies_the_mserp_table_as_finance_and_operations()
    {
        var fo = VirtualTableMetadataParser.Parse(Sample).Single(t => t.LogicalName == "mserp_custcustomerv3entity");

        Assert.Equal(VirtualTableSource.FinanceAndOperations, fo.Source);
        Assert.True(fo.IsFinanceAndOperations);
        Assert.Equal("Customer V3", fo.DisplayName);
        Assert.Equal("CustCustomerV3Entity", fo.ExternalName);
        Assert.True(fo.IsManaged);
    }

    [Fact]
    public void Classifies_a_non_mserp_provider_as_other()
    {
        var other = VirtualTableMetadataParser.Parse(Sample).Single(t => t.LogicalName == "contoso_sqlproduct");

        Assert.Equal(VirtualTableSource.Other, other.Source);
        Assert.False(other.IsFinanceAndOperations);
        Assert.False(other.IsManaged);
    }

    [Fact]
    public void Returns_empty_for_valid_empty_collection() =>
        Assert.Empty(VirtualTableMetadataParser.Parse("{\"value\":[]}"));

    [Fact]
    public void A_virtual_table_with_only_an_external_name_is_still_included()
    {
        var tables = VirtualTableMetadataParser.Parse(
            "{\"value\":[{\"LogicalName\":\"mserp_x\",\"ExternalName\":\"X\"}]}");

        Assert.Single(tables);
        Assert.Equal("mserp_x", tables[0].LogicalName);
    }
}
