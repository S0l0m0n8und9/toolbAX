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
}
