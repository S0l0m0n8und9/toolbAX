using System;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="FoFilterFieldCaser"/> — the count-time pass that reconciles a converted dual-write
/// source filter's identifiers with the F&amp;O entity's real property names (#204). F&amp;O's OData
/// property lookup is case-sensitive PascalCase while X++ source filters carry staging-case names, so
/// without this every such leg count is a 400. Literal-aware in the same way as
/// <see cref="DualWriteFilterConverter"/> (#162): nothing inside a quoted literal is touched.
/// </summary>
public class FoFilterFieldCaserTests
{
    // The properties of a CustomerV3-shaped entity, in the casing F&O really exposes them under.
    private static readonly string[] Properties =
    {
        "IsOneTimeCustomer", "CustomerAccount", "OrganizationName", "LineStatus",
    };

    [Fact]
    public void Staging_case_identifiers_are_corrected_to_the_propertys_exact_casing()
    {
        // #204's live matrix, rows 2 and 4: 'ISONETIMECUSTOMER' is "no such property" on CustomerV3,
        // 'IsOneTimeCustomer' is the spelling that answers 200.
        var result = FoFilterFieldCaser.Correct(
            "(ISONETIMECUSTOMER ne Microsoft.Dynamics.DataEntities.NoYes'Yes')", Properties);

        Assert.Equal("(IsOneTimeCustomer ne Microsoft.Dynamics.DataEntities.NoYes'Yes')", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_already_correctly_cased_identifier_is_left_as_is()
    {
        var result = FoFilterFieldCaser.Correct("CustomerAccount eq 'US-001'", Properties);

        Assert.Equal("CustomerAccount eq 'US-001'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void The_enum_namespace_and_type_tokens_are_never_re_cased_or_reported()
    {
        // Adversarial property list: names that collide with the qualified enum literal's own tokens. The
        // enum form is live-proven good — re-casing any part of it would break a working filter, and
        // reporting one would skip a countable leg.
        var shadowing = new[] { "A", "microsoft", "dynamics", "dataentities", "noyes", "yes" };

        var result = FoFilterFieldCaser.Correct(
            "A eq Microsoft.Dynamics.DataEntities.NoYes'Yes'", shadowing);

        Assert.Equal("A eq Microsoft.Dynamics.DataEntities.NoYes'Yes'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_unqualified_enum_type_token_is_also_left_alone()
    {
        // The bare "Type'Member'" spelling is still part of the enum-literal form, not a field reference.
        var result = FoFilterFieldCaser.Correct("LINESTATUS ne SalesStatus'Invoiced'", Properties);

        Assert.Equal("LineStatus ne SalesStatus'Invoiced'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_identifier_matching_no_property_is_reported()
    {
        var result = FoFilterFieldCaser.Correct("QUOTATIONNUMBER eq 'Q-1' and LINESTATUS eq 2", Properties);

        Assert.Equal(new[] { "QUOTATIONNUMBER" }, result.UnknownFields);   // reported as written
        Assert.Equal("QUOTATIONNUMBER eq 'Q-1' and LineStatus eq 2", result.Filter); // the rest still corrected
    }

    [Fact]
    public void An_unknown_identifier_is_reported_once_however_often_it_appears()
    {
        var result = FoFilterFieldCaser.Correct("NOSUCHFIELD eq 1 or NoSuchField eq 2", Properties);

        Assert.Equal(new[] { "NOSUCHFIELD" }, result.UnknownFields);
    }

    [Fact]
    public void Odata_keywords_and_literals_are_not_treated_as_fields()
    {
        var result = FoFilterFieldCaser.Correct(
            "not (CUSTOMERACCOUNT eq null or LINESTATUS ne 2) and ISONETIMECUSTOMER eq true", Properties);

        Assert.Empty(result.UnknownFields);
        Assert.Equal("not (CustomerAccount eq null or LineStatus ne 2) and IsOneTimeCustomer eq true",
            result.Filter);
    }

    [Fact]
    public void Identifiers_inside_a_string_literal_are_untouched()
    {
        var result = FoFilterFieldCaser.Correct(
            "ORGANIZATIONNAME eq 'ISONETIMECUSTOMER ne NOSUCHFIELD'", Properties);

        Assert.Equal("OrganizationName eq 'ISONETIMECUSTOMER ne NOSUCHFIELD'", result.Filter);
        Assert.Empty(result.UnknownFields); // NOSUCHFIELD is literal text, not a field reference
    }

    [Fact]
    public void An_escaped_quote_does_not_end_the_literal()
    {
        // #162's literal-tracking rule: '' inside a literal is an escaped quote, so the identifiers after
        // it are still literal content — not fields to correct or report.
        var result = FoFilterFieldCaser.Correct(
            "ORGANIZATIONNAME eq 'O''BRIEN CUSTOMERACCOUNT' and LINESTATUS eq 1", Properties);

        Assert.Equal("OrganizationName eq 'O''BRIEN CUSTOMERACCOUNT' and LineStatus eq 1", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_pathed_identifier_is_passed_through_rather_than_guessed_at()
    {
        // A navigation path can't be validated against one entity's property list, so neither segment is
        // re-cased or reported — passing it through lets the server have the last word (#162 doctrine).
        var result = FoFilterFieldCaser.Correct("PrimaryContact/CUSTOMERACCOUNT eq 'x'", Properties);

        Assert.Equal("PrimaryContact/CUSTOMERACCOUNT eq 'x'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_function_name_is_not_treated_as_a_field()
    {
        var result = FoFilterFieldCaser.Correct("contains(ORGANIZATIONNAME,'ACME')", Properties);

        Assert.Equal("contains(OrganizationName,'ACME')", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void Numbers_are_not_treated_as_fields()
    {
        var result = FoFilterFieldCaser.Correct("LINESTATUS eq 2 and CREDITLIMIT ge 1000", Properties);

        Assert.Equal(new[] { "CREDITLIMIT" }, result.UnknownFields); // the number is not reported
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void An_empty_filter_comes_back_empty(string? input, string expected)
    {
        var result = FoFilterFieldCaser.Correct(input, Properties);

        Assert.Equal(expected, result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void Without_property_names_nothing_is_corrected_or_reported()
    {
        // Nothing to validate against is not the same as "every field is wrong" — the caller sends the
        // filter as-is rather than declaring a countable leg not-countable.
        var result = FoFilterFieldCaser.Correct("ISONETIMECUSTOMER eq 1", Array.Empty<string>());

        Assert.Equal("ISONETIMECUSTOMER eq 1", result.Filter);
        Assert.Empty(result.UnknownFields);
    }
}
