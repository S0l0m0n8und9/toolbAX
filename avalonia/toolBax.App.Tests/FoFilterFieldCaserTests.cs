using System;
using System.Collections.Generic;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="FoFilterFieldCaser"/> — the count-time pass that reconciles a converted dual-write
/// source filter's identifiers with the F&amp;O entity's real property names (#204), and upgrades a quoted
/// string member to a qualified enum literal when it's compared against an enum-typed property (#207).
/// F&amp;O's OData property lookup is case-sensitive PascalCase while X++ source filters carry staging-case
/// names, so without this every such leg count is a 400. Literal-aware in the same way as
/// <see cref="DualWriteFilterConverter"/> (#162): nothing inside a quoted literal is touched, unless the
/// walk recognises it as a member of the enum property it's compared against.
/// </summary>
public class FoFilterFieldCaserTests
{
    private static EntityField StringField(string name) => new(name, "String", true);

    private static EntityField EnumField(string name, string local, string qualified) =>
        new(name, "Enum", true, EnumType: local, QualifiedEnumType: qualified);

    // The properties of a CustomerV3-shaped entity, in the casing F&O really exposes them under. LineStatus
    // is enum-typed — #207's live leg (RicohDev, 2026-08-13): TransferOrderLines.LineStatus is
    // InventTransferRemainStatus.
    private static readonly EntityField[] Properties =
    {
        StringField("IsOneTimeCustomer"),
        StringField("CustomerAccount"),
        StringField("OrganizationName"),
        EnumField("LineStatus", "InventTransferRemainStatus", "Microsoft.Dynamics.DataEntities.InventTransferRemainStatus"),
    };

    // Members cached for "InventTransferRemainStatus" only; case-insensitive on the type name, null for
    // anything else (the "not cached yet" shape).
    private static Func<string, IReadOnlyList<string>?> Members(string type, params string[] members) =>
        candidate => string.Equals(candidate, type, StringComparison.OrdinalIgnoreCase) ? members : null;

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
        var shadowing = new[]
        {
            StringField("A"), StringField("microsoft"), StringField("dynamics"),
            StringField("dataentities"), StringField("noyes"), StringField("yes"),
        };

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
        var result = FoFilterFieldCaser.Correct("ISONETIMECUSTOMER eq 1", Array.Empty<EntityField>());

        Assert.Equal("ISONETIMECUSTOMER eq 1", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_quoted_member_against_an_enum_property_becomes_a_qualified_enum_literal()
    {
        // #207's live leg (RicohDev, 2026-08-13): LineStatus is enum-typed and 'None' is one of its cached
        // members, so the literal is upgraded in the same pass that fixes the field's casing.
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("(LINESTATUS ne 'None')", Properties, enumMembers);

        Assert.Equal(
            "(LineStatus ne Microsoft.Dynamics.DataEntities.InventTransferRemainStatus'None')", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void The_enum_members_own_casing_wins_over_the_filters()
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("LineStatus ne 'none'", Properties, enumMembers);

        Assert.Equal(
            "LineStatus ne Microsoft.Dynamics.DataEntities.InventTransferRemainStatus'None'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_quoted_string_matching_no_member_is_passed_through()
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("LineStatus ne 'Bogus'", Properties, enumMembers);

        Assert.Equal("LineStatus ne 'Bogus'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_string_propertys_literal_is_never_typed()
    {
        // 'None' is a member of the OTHER field's (LineStatus's) enum, but OrganizationName is String-typed.
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("OrganizationName eq 'None'", Properties, enumMembers);

        Assert.Equal("OrganizationName eq 'None'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_literal_inside_a_function_is_not_typed()
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("contains(LINESTATUS,'None')", Properties, enumMembers);

        Assert.Equal("contains(LineStatus,'None')", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void A_numeric_literal_against_an_enum_property_stays_the_documented_limit()
    {
        // #207's other live leg (RicohDev, 2026-08-13): TransferOrderStatus ne 2 — no ordinal->member
        // mapping exists in our metadata, so this stays a documented limit.
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct("LineStatus ne 2", Properties, enumMembers);

        Assert.Equal("LineStatus ne 2", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_enum_property_whose_members_are_not_cached_passes_the_literal_through()
    {
        Func<string, IReadOnlyList<string>?> noMembersCached = _ => null;

        var result = FoFilterFieldCaser.Correct("LineStatus ne 'None'", Properties, noMembersCached);

        Assert.Equal("LineStatus ne 'None'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_enum_property_without_a_qualified_type_is_not_typed()
    {
        // Members are present and DO match (case-insensitively) — deliberately spelled differently from the
        // filter's own casing ('none' vs the cached 'None'), so a wrongly-fabricated upgrade would be
        // visible as a casing change even though there's no qualified type to prefix it with. The only thing
        // standing between this and a fabricated type reference is the qualified-type guard itself.
        var properties = new[] { new EntityField("Status", "Enum", true, EnumType: "X") };
        Func<string, IReadOnlyList<string>?> members = _ => new[] { "None" };

        var result = FoFilterFieldCaser.Correct("STATUS eq 'none'", properties, members);

        Assert.Equal("Status eq 'none'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("ne")]
    [InlineData("gt")]
    [InlineData("lt")]
    [InlineData("ge")]
    [InlineData("le")]
    public void Each_comparison_operator_carries_the_upgrade(string op)
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct($"LineStatus {op} 'None'", Properties, enumMembers);

        Assert.Equal(
            $"LineStatus {op} Microsoft.Dynamics.DataEntities.InventTransferRemainStatus'None'", result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void Only_the_enum_propertys_own_literal_is_typed()
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var result = FoFilterFieldCaser.Correct(
            "LineStatus eq 'None' and ORGANIZATIONNAME eq 'None'", Properties, enumMembers);

        Assert.Equal(
            "LineStatus eq Microsoft.Dynamics.DataEntities.InventTransferRemainStatus'None' and OrganizationName eq 'None'",
            result.Filter);
        Assert.Empty(result.UnknownFields);
    }

    [Fact]
    public void An_escaped_quote_in_an_enum_comparison_does_not_derail_the_walk()
    {
        var enumMembers = Members("InventTransferRemainStatus", "None", "Shipped", "Received");

        var noMatch = FoFilterFieldCaser.Correct(
            "LineStatus ne 'O''BRIEN' and ORGANIZATIONNAME eq 'x'", Properties, enumMembers);

        Assert.Equal("LineStatus ne 'O''BRIEN' and OrganizationName eq 'x'", noMatch.Filter);
        Assert.Empty(noMatch.UnknownFields);

        var upgraded = FoFilterFieldCaser.Correct(
            "LINESTATUS ne 'None' and ORGANIZATIONNAME eq 'x'", Properties, enumMembers);

        Assert.Equal(
            "LineStatus ne Microsoft.Dynamics.DataEntities.InventTransferRemainStatus'None' and OrganizationName eq 'x'",
            upgraded.Filter);
        Assert.Empty(upgraded.UnknownFields);
    }
}
