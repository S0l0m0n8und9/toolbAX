using FoToolbox.Core.OData;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ODataMetadataIndexParserTests
{
    // ── ParseIndex ────────────────────────────────────────────────────────────────────────────────────
    // ParseIndex produces the ENTIRE Metadata Browser master list (via CatalogService), and had no tests
    // at all (#167): a miscount, a missed EntitySet, or a broken type-reference fallback would have shown
    // up only as a wrong list in the running app.

    // One container, two sets. Customers' type is referenced by its FULL name (namespace-qualified, the
    // usual F&O shape); Vendors' by a namespace the schema never declares, so only the short-name fallback
    // can resolve its counts. Products has an EnumType and a Collection(...) property.
    private const string IndexXml = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Microsoft.Dynamics.DataEntities" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Resources">
        <EntitySet Name="Customers" EntityType="Microsoft.Dynamics.DataEntities.Customer" />
        <EntitySet Name="Vendors" EntityType="Some.Other.Namespace.Vendor" />
        <EntitySet Name="Products" EntityType="Microsoft.Dynamics.DataEntities.Product" />
      </EntityContainer>
      <EntityType Name="Customer">
        <Key>
          <PropertyRef Name="CustomerAccount" />
        </Key>
        <Property Name="CustomerAccount" Type="Edm.String" />
        <Property Name="OrganizationName" Type="Edm.String" />
        <Property Name="CreditLimit" Type="Edm.Decimal" />
        <NavigationProperty Name="SalesOrders" Type="Collection(Microsoft.Dynamics.DataEntities.SalesOrder)" />
      </EntityType>
      <EntityType Name="Vendor">
        <Property Name="VendorAccountNumber" Type="Edm.String" />
        <NavigationProperty Name="Invoices" Type="Collection(Microsoft.Dynamics.DataEntities.Invoice)" />
        <NavigationProperty Name="PrimaryContact" Type="Microsoft.Dynamics.DataEntities.Contact" />
      </EntityType>
      <EntityType Name="Product">
        <Property Name="ProductNumber" Type="Edm.String" />
        <Property Name="SearchNames" Type="Collection(Edm.String)" />
        <Property Name="Status" Type="Microsoft.Dynamics.DataEntities.ProductStatus" />
      </EntityType>
      <EnumType Name="ProductStatus">
        <Member Name="Active" Value="0" />
        <Member Name="Blocked" Value="1" />
        <Member Name="Retired" Value="2" />
      </EnumType>
      <EnumType Name="NoMembers" />
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";

    [Fact]
    public void ParseIndex_Lists_Entity_Sets_With_Property_And_Navigation_Counts()
    {
        var index = ODataMetadataIndexParser.ParseIndex(IndexXml);

        Assert.Equal(new[] { "Customers", "Vendors", "Products" }, index.Entities.Select(e => e.Name).ToArray());

        var customers = index.Entities.Single(e => e.Name == "Customers");
        Assert.Equal(3, customers.PropertyCount);      // Key/PropertyRef is not a property
        Assert.Equal(1, customers.NavigationCount);
    }

    [Fact]
    public void ParseIndex_Resolves_A_Type_Reference_By_Short_Name_When_The_Namespace_Does_Not_Match()
    {
        // Vendors points at "Some.Other.Namespace.Vendor", which no schema declares; the counts must still
        // come from the EntityType named "Vendor" rather than silently falling back to 0/0.
        var index = ODataMetadataIndexParser.ParseIndex(IndexXml);

        var vendors = index.Entities.Single(e => e.Name == "Vendors");
        Assert.Equal(1, vendors.PropertyCount);
        Assert.Equal(2, vendors.NavigationCount);
    }

    [Fact]
    public void ParseIndex_Counts_A_Collection_Property_As_A_Property_Not_A_Navigation()
    {
        // Collection(Edm.String) is a multi-valued PRIMITIVE property. Treating "Collection(" as the marker
        // of a navigation would move it into the wrong count and understate the field list.
        var index = ODataMetadataIndexParser.ParseIndex(IndexXml);

        var products = index.Entities.Single(e => e.Name == "Products");
        Assert.Equal(3, products.PropertyCount);   // ProductNumber, SearchNames (a collection), Status
        Assert.Equal(0, products.NavigationCount);
    }

    [Fact]
    public void ParseIndex_Extracts_Enum_Members_Under_The_Qualified_Type_Name()
    {
        var index = ODataMetadataIndexParser.ParseIndex(IndexXml);

        var status = index.Enums.Single(e => e.Name == "Microsoft.Dynamics.DataEntities.ProductStatus");
        Assert.Equal(new[] { "Active", "Blocked", "Retired" }, status.Members.ToArray());

        // A self-closing EnumType yields an empty member list, not a skipped/duplicated entry.
        var empty = index.Enums.Single(e => e.Name == "Microsoft.Dynamics.DataEntities.NoMembers");
        Assert.Empty(empty.Members);
    }

    [Fact]
    public void ParseIndex_Falls_Back_To_Entity_Types_When_There_Are_No_Entity_Sets()
    {
        // Some $metadata documents (and trimmed/partial responses) declare types without a container. The
        // master list must still show something rather than coming back empty.
        const string noSets = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityType Name="Orphan">
        <Property Name="Id" Type="Edm.String" />
        <Property Name="Name" Type="Edm.String" />
        <NavigationProperty Name="Related" Type="Default.Other" />
      </EntityType>
      <EntityType Name="Bare" />
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";

        var index = ODataMetadataIndexParser.ParseIndex(noSets);

        // Listed by EntityType SHORT name (what the browser can actually query by).
        Assert.Equal(new[] { "Orphan", "Bare" }, index.Entities.Select(e => e.Name).ToArray());
        var orphan = index.Entities.Single(e => e.Name == "Orphan");
        Assert.Equal(2, orphan.PropertyCount);
        Assert.Equal(1, orphan.NavigationCount);

        var bare = index.Entities.Single(e => e.Name == "Bare");
        Assert.Equal(0, bare.PropertyCount);
        Assert.Equal(0, bare.NavigationCount);
    }

    [Fact]
    public void ParseIndex_Reports_Zero_Counts_For_A_Set_Whose_Type_Is_Missing_Or_Unnamed()
    {
        // An EntitySet with no resolvable EntityType still belongs on the list (it is queryable) — with
        // honest zero counts rather than being dropped.
        const string danglingRef = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Resources">
        <EntitySet Name="Ghosts" EntityType="Default.NoSuchType" />
        <EntitySet Name="Untyped" />
        <!-- The unnamed set is deliberately LAST. ParseIndex answers a blank Name with reader.Skip() +
             continue, and Skip() on a self-closing element already lands on the NEXT sibling, so the
             loop's own Read() then steps over it: an unnamed set in the middle of a container silently
             swallows the set that follows it. That is a live parser defect, filed separately rather than
             fixed here — do not reorder this fixture expecting the sibling to survive. -->
        <EntitySet EntityType="Default.Nameless" />
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";

        var index = ODataMetadataIndexParser.ParseIndex(danglingRef);

        // The nameless set is dropped (nothing to show); the other two are listed with zero counts.
        Assert.Equal(new[] { "Ghosts", "Untyped" }, index.Entities.Select(e => e.Name).ToArray());
        Assert.All(index.Entities, e =>
        {
            Assert.Equal(0, e.PropertyCount);
            Assert.Equal(0, e.NavigationCount);
        });
        Assert.Empty(index.Enums);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseIndex_Returns_An_Empty_Index_For_No_Document_But_Keeps_The_Etag(string? rawXml)
    {
        // CatalogService caches by ETag, so the tag must survive even when the body is unusable.
        var index = ODataMetadataIndexParser.ParseIndex(rawXml!, "W/\"abc123\"");

        Assert.Empty(index.Entities);
        Assert.Empty(index.Enums);
        Assert.Equal("W/\"abc123\"", index.ETag);
    }

    [Fact]
    public void ParseIndex_Carries_The_Etag_Through_A_Parsed_Document()
    {
        var index = ODataMetadataIndexParser.ParseIndex(IndexXml, "W/\"v2\"");

        Assert.Equal("W/\"v2\"", index.ETag);
        Assert.NotEmpty(index.Entities);
    }

    // ── TryParseEntityDetails ─────────────────────────────────────────────────────────────────────────

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
