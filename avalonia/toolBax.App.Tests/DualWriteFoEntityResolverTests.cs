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

    // --- #209: what can and cannot be pasted into /data/{entity} ---

    [Fact]
    public void A_display_style_schema_is_not_a_usable_entity_name()
    {
        // The live 404: "GET /data/CDS released distinct products". Spaces (and punctuation) can't appear in
        // an OData entity-set name, so callers must treat such a value as unresolved.
        Assert.False(DualWriteFoEntityResolver.IsUsableEntityName("CDS released distinct products"));
        Assert.False(DualWriteFoEntityResolver.IsUsableEntityName("VendVendorV2Entity (Distinct)"));
        Assert.False(DualWriteFoEntityResolver.IsUsableEntityName("Cust-Customer"));
        Assert.False(DualWriteFoEntityResolver.IsUsableEntityName("   "));
        Assert.False(DualWriteFoEntityResolver.IsUsableEntityName(null));
    }

    [Fact]
    public void An_identifier_shaped_name_is_usable()
    {
        Assert.True(DualWriteFoEntityResolver.IsUsableEntityName("CustCustomerV3"));
        Assert.True(DualWriteFoEntityResolver.IsUsableEntityName("my_custom_entity9"));
    }

    [Fact]
    public void A_catalog_entry_that_cannot_be_an_entity_name_is_not_returned_as_a_match()
    {
        // The resolver only ever returns names the caller supplied, so this is belt-and-braces: a catalogue
        // carrying a display-style entry must not become the value a count is fired at.
        var resolved = DualWriteFoEntityResolver.Resolve(
            "released distinct products", null, new[] { "released distinct products" });

        Assert.Equal(string.Empty, resolved);
    }
}
