using System;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="DualWriteFoEntityResolver"/> — the fuzzy match from a dual-write source schema to
/// an F&amp;O OData entity set (used to default the Row counts "F&amp;O entity"). Pure, faithfully ported
/// from the WPF scorer (drops the "Entity" suffix / version tokens, scores prefix/contains/overlap, and
/// refuses ambiguous matches).
/// </summary>
public class DualWriteFoEntityResolverTests
{
    private static readonly string[] Catalog =
    {
        "VendVendorsV2", "VendVendorV2", "CustCustomerV3", "SalesOrderHeadersV2", "ReleasedProductsV2",
    };

    [Fact]
    public void Resolves_a_data_entity_to_the_matching_set()
    {
        var resolved = DualWriteFoEntityResolver.Resolve("VendVendorV2Entity", "VendVendorV2Entity (Distinct)", Catalog);
        Assert.Equal("VendVendorV2", resolved);
    }

    [Fact]
    public void Resolves_via_the_distinct_name_when_the_schema_is_blank()
    {
        var resolved = DualWriteFoEntityResolver.Resolve(null, "CustCustomerV3Entity (Distinct)", Catalog);
        Assert.Equal("CustCustomerV3", resolved);
    }

    [Fact]
    public void Returns_empty_when_there_is_no_confident_match()
    {
        var resolved = DualWriteFoEntityResolver.Resolve("CompletelyUnrelatedThing", null, Catalog);
        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void Returns_empty_for_an_empty_catalog()
    {
        Assert.Equal(string.Empty, DualWriteFoEntityResolver.Resolve("VendVendorV2Entity", null, Array.Empty<string>()));
    }

    [Fact]
    public void Refuses_an_ambiguous_match()
    {
        // Two distinct, equally-plausible candidates (same length + structure) → no confident winner.
        var resolved = DualWriteFoEntityResolver.Resolve("Account", null, new[] { "AccountX", "AccountY" });
        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void Tolerates_null_and_empty_schemas()
    {
        Assert.Equal(string.Empty, DualWriteFoEntityResolver.Resolve(null, null, Catalog));
        Assert.Equal(string.Empty, DualWriteFoEntityResolver.Resolve("   ", "", Catalog));
    }
}
