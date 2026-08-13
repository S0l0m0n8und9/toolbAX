using System;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies the Row-counts verdict logic (<see cref="MapLegCountRow"/>) only claims Match/Mismatch when
/// the two numbers are actually comparable (#159): Dataverse caps <c>@odata.count</c> at 5,000, and a
/// forward-only map filters the F&amp;O side while counting the whole CE table. Pure view-model logic, no
/// UI / network → runs on Linux CI.
/// </summary>
public class MapLegCountRowTests
{
    // A leg with exactly the filters under test. foFilter lands on the F&O count (it is the leg's
    // already-converted OData source filter); ceFilter is the reversed source filter used for CE.
    private static MapLegCountRow Row(string foFilter = "", string ceFilter = "") =>
        new(new DwMapLeg(
            LegId: "leg-1",
            SourceSchema: "CustCustomerV3Entity",
            SourceSchemaDistinctName: "CustCustomerV3Entity (Distinct)",
            DestinationSchema: "accounts",
            SourceEnvironmentType: "AX",
            DestinationEnvironmentType: "CRM",
            SourceFilter: foFilter,
            ReversedSourceFilter: ceFilter,
            SourceFilterOData: foFilter,
            FieldMappings: 1));

    // --- the Dataverse count cap ---

    [Fact]
    public void A_capped_ce_count_renders_with_a_plus_and_no_verdict()
    {
        // The reported bug: a 42,000-row table in sync with F&O came back as "CE rows 5,000 / Mismatch".
        var row = Row();
        row.CeCountCapped = true;
        row.CeCount = 5000;
        row.FoCount = 42_000;

        Assert.Equal("5,000+", row.CeCountLabel);
        Assert.Equal("Unknown (CE count capped)", row.ComparisonLabel);
    }

    [Fact]
    public void A_capped_ce_count_is_never_a_match_even_when_the_numbers_are_equal()
    {
        // The other half of the bug: an F&O side that happens to hold exactly 5,000 rows must not read as
        // a Match against a capped CE count — the CE number is a floor, so equality proves nothing.
        var row = Row();
        row.CeCountCapped = true;
        row.CeCount = 5000;
        row.FoCount = 5000;

        Assert.NotEqual("Match", row.ComparisonLabel);
        Assert.Equal("Unknown (CE count capped)", row.ComparisonLabel);
    }

    [Fact]
    public void An_uncapped_count_on_the_cap_boundary_still_compares_normally()
    {
        // A genuine 5,000-row table (the cap annotation said the limit was not exceeded) is an exact
        // total, so the verdict is a real one.
        var row = Row();
        row.CeCount = 5000;
        row.FoCount = 5000;

        Assert.Equal("5,000", row.CeCountLabel);
        Assert.Equal("Match", row.ComparisonLabel);
    }

    // --- one-sided (forward-only) filters ---

    [Fact]
    public void A_filter_on_the_fo_side_only_is_not_comparable()
    {
        // Forward-only map: sourceFilter set, reversedSourceFilter empty → F&O counts a subset while CE
        // counts the whole table. Previously a guaranteed bogus "Mismatch".
        var row = Row(foFilter: "CustomerGroupId eq 'DOM'");
        row.FoCount = 250;
        row.CeCount = 1000;

        Assert.False(row.FiltersComparable);
        Assert.Equal("Not comparable (filters differ)", row.ComparisonLabel);
    }

    [Fact]
    public void A_filter_on_the_ce_side_only_is_not_comparable()
    {
        var row = Row(ceFilter: "accounttype eq 'customer'");
        row.FoCount = 1000;
        row.CeCount = 250;

        Assert.False(row.FiltersComparable);
        Assert.Equal("Not comparable (filters differ)", row.ComparisonLabel);
    }

    [Fact]
    public void Both_sides_still_count_and_display_when_the_filters_differ()
    {
        // Not-comparable suppresses the verdict, not the numbers — the counts are still the useful output.
        var row = Row(foFilter: "CustomerGroupId eq 'DOM'");
        row.FoCount = 250;
        row.CeCount = 42_000;

        Assert.Equal("250", row.FoCountLabel);
        Assert.Equal("42,000", row.CeCountLabel);
    }

    [Fact]
    public void Filters_on_both_sides_compare_as_before()
    {
        var match = Row(foFilter: "CustomerGroupId eq 'DOM'", ceFilter: "accounttype eq 'customer'");
        match.FoCount = 250;
        match.CeCount = 250;
        Assert.True(match.FiltersComparable);
        Assert.Equal("Match", match.ComparisonLabel);

        var mismatch = Row(foFilter: "CustomerGroupId eq 'DOM'", ceFilter: "accounttype eq 'customer'");
        mismatch.FoCount = 999;
        mismatch.CeCount = 250;
        Assert.Equal("Mismatch", mismatch.ComparisonLabel);
    }

    [Fact]
    public void No_filter_on_either_side_compares_as_before()
    {
        var match = Row();
        match.FoCount = 1000;
        match.CeCount = 1000;
        Assert.True(match.FiltersComparable);
        Assert.Equal("Match", match.ComparisonLabel);

        var mismatch = Row();
        mismatch.FoCount = 999;
        mismatch.CeCount = 1000;
        Assert.Equal("Mismatch", mismatch.ComparisonLabel);
    }

    [Fact]
    public void An_uncounted_row_has_no_verdict()
    {
        var row = Row(foFilter: "CustomerGroupId eq 'DOM'");
        row.FoCount = 250;

        // Filters differ, but nothing has been counted on the CE side yet — "—" still wins.
        Assert.Equal("—", row.ComparisonLabel);
        Assert.Equal("—", row.CeCountLabel);
    }

    [Fact]
    public void Filter_asymmetry_is_reported_ahead_of_the_cap()
    {
        // Both problems at once: the mismatched populations are the more actionable finding (and are
        // knowable before counting), so they lead.
        var row = Row(foFilter: "CustomerGroupId eq 'DOM'");
        row.CeCountCapped = true;
        row.CeCount = 5000;
        row.FoCount = 250;

        Assert.Equal("Not comparable (filters differ)", row.ComparisonLabel);
    }

    // --- the applied filter is visible per side (ToolTip source) ---

    [Fact]
    public void Each_side_reports_the_filter_its_count_was_taken_with()
    {
        var filtered = Row(foFilter: "CustomerGroupId eq 'DOM'");

        Assert.True(filtered.FoFilterApplied);
        Assert.Equal("Counted with filter: CustomerGroupId eq 'DOM'", filtered.FoFilterTip);
        Assert.False(filtered.CeFilterApplied);
        Assert.Equal("Counted unfiltered", filtered.CeFilterTip);
    }

    [Fact]
    public void A_whitespace_only_filter_counts_as_no_filter()
    {
        var row = Row(foFilter: "   ");

        Assert.False(row.FoFilterApplied);
        Assert.Equal("Counted unfiltered", row.FoFilterTip);
        Assert.True(row.FiltersComparable); // neither side filtered
    }

    // --- #210: a snapshot total is a real total, but not an exact-as-of-now one ---

    [Fact]
    public void A_snapshot_ce_total_renders_with_a_tilde_and_an_approximate_verdict()
    {
        // RetrieveTotalRecordCount answers from a snapshot up to 24 hours old, so equality with a live F&O
        // number is strong evidence and not proof — the verdict says approximately, not "Match".
        var row = Row();
        row.CeCountSnapshot = true;
        row.CeCount = 42_317;
        row.FoCount = 42_317;

        Assert.Equal("≈42,317", row.CeCountLabel);
        Assert.Equal("≈ Match", row.ComparisonLabel);
    }

    [Fact]
    public void A_snapshot_ce_total_that_disagrees_is_an_approximate_mismatch()
    {
        var row = Row();
        row.CeCountSnapshot = true;
        row.CeCount = 42_317;
        row.FoCount = 40_000;

        Assert.Equal("≈ Mismatch", row.ComparisonLabel);
    }

    [Fact]
    public void A_snapshot_total_carries_its_caveat_in_the_ce_tooltip()
    {
        // The cell is narrow, so "≈42,317" is the label and the ≤24h caveat lives in the existing tooltip.
        var row = Row();
        row.CeCountSnapshot = true;
        row.CeCount = 42_317;

        Assert.Contains("snapshot", row.CeFilterTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("24h", row.CeFilterTip);
        Assert.Equal("Counted unfiltered", Row().CeFilterTip); // …and only when the number is one
    }

    [Fact]
    public void A_one_sided_filter_still_outranks_a_snapshot_total()
    {
        // The verdict tiers keep their order: differing populations are the more actionable finding.
        var row = Row(foFilter: "CustomerGroupId eq 'DOM'");
        row.CeCountSnapshot = true;
        row.CeCount = 42_317;
        row.FoCount = 250;

        Assert.Equal("Not comparable (filters differ)", row.ComparisonLabel);
    }

    // --- #209: a late entity resolution replaces the default, never the user's correction ---

    [Fact]
    public void A_later_resolution_replaces_a_defaulted_fo_entity()
    {
        var row = Row();
        Assert.Equal("CustCustomerV3", row.FoEntity);   // the "drop the Entity suffix" guess
        Assert.True(row.FoEntityIsDefault);

        row.AdoptResolvedFoEntity("CustCustomersV3");    // the catalogue arrived and knows better

        Assert.Equal("CustCustomersV3", row.FoEntity);
        Assert.True(row.FoEntityIsDefault);              // still un-corrected, so still replaceable
    }

    [Fact]
    public void A_later_resolution_leaves_a_user_corrected_fo_entity_alone()
    {
        var row = Row();
        row.FoEntity = "MyOwnEntity";

        row.AdoptResolvedFoEntity("CustCustomersV3");

        Assert.Equal("MyOwnEntity", row.FoEntity);
        Assert.False(row.FoEntityIsDefault);
    }

    [Fact]
    public void A_blank_resolution_leaves_the_default_in_place()
    {
        var row = Row();

        row.AdoptResolvedFoEntity(string.Empty);

        Assert.Equal("CustCustomerV3", row.FoEntity);
    }
}
