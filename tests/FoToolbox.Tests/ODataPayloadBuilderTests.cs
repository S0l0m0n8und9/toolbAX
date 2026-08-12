using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ODataPayloadBuilderTests
{
    // A one-property entity carrying one value, so a test can assert on a single field's coercion.
    private static ODataPayloadBuildResult Coerce(string edmType, string value) =>
        ODataPayloadBuilder.BuildPayloadJson(
            new ODataEntity(
                "TestEntity",
                new[] { new ODataProperty("F", edmType, Nullable: true, IsKey: false, IsMandatory: false) },
                Array.Empty<ODataNavigationProperty>()),
            new[] { new ODataFieldValue("F", Include: true, Value: value) });

    // The single coerced field, asserting the build succeeded first so a failure names the issue.
    private static JsonElement Coerced(string edmType, string value)
    {
        var result = Coerce(edmType, value);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        using var doc = JsonDocument.Parse(result.Json!);
        return doc.RootElement.GetProperty("F").Clone();
    }

    [Fact]
    public void BuildPayloadJson_Builds_Typed_Json_And_Validates_Enums()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
                new ODataProperty("Count", "Edm.Int32", Nullable: true, IsKey: false, IsMandatory: false),
                new ODataProperty("IsActive", "Edm.Boolean", Nullable: false, IsKey: false, IsMandatory: false),
                new ODataProperty("Category", "My.EnumType", Nullable: false, IsKey: false, IsMandatory: false),
                new ODataProperty("OptionalNote", "Edm.String", Nullable: true, IsKey: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["My.EnumType"] = new[] { "A", "B" }
        };

        var fields = new[]
        {
            new ODataFieldValue("Name", Include: true, Value: "hello"),
            new ODataFieldValue("Count", Include: true, Value: "42"),
            new ODataFieldValue("IsActive", Include: true, Value: "true"),
            new ODataFieldValue("Category", Include: true, Value: "B"),
            new ODataFieldValue("OptionalNote", Include: true, Value: "null"),
        };

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields, enums);
        Assert.True(result.Ok);
        Assert.NotNull(result.Json);

        using var doc = JsonDocument.Parse(result.Json!);
        var root = doc.RootElement;
        Assert.Equal("hello", root.GetProperty("Name").GetString());
        Assert.Equal(42, root.GetProperty("Count").GetInt32());
        Assert.True(root.GetProperty("IsActive").GetBoolean());
        Assert.Equal("B", root.GetProperty("Category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("OptionalNote").ValueKind);
    }

    [Fact]
    public void BuildPayloadJson_Fails_When_Mandatory_Missing()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
            },
            Array.Empty<ODataNavigationProperty>());

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, Array.Empty<ODataFieldValue>(), enforceMandatory: true);
        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("mandatory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPayloadJson_Omits_Optional_Blanks()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
                new ODataProperty("Optional", "Edm.String", Nullable: true, IsKey: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var fields = new[]
        {
            new ODataFieldValue("Name", Include: true, Value: "x"),
            new ODataFieldValue("Optional", Include: true, Value: "  "),
        };

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields);
        Assert.True(result.Ok);

        using var doc = JsonDocument.Parse(result.Json!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("Name", out _));
        Assert.False(root.TryGetProperty("Optional", out _));
    }

    // --- Clear-field semantics: an included-but-blank field on a PATCH means "clear me" (#158) ---

    // Two optional properties differing only in nullability, so one build can exercise both readings of
    // "included but blank" without mandatory enforcement getting in the way.
    private static readonly ODataEntity NullabilityEntity = new(
        "TestEntity",
        new[]
        {
            new ODataProperty("Nullable", "Edm.String", Nullable: true, IsKey: false, IsMandatory: false),
            new ODataProperty("NotNullable", "Edm.String", Nullable: false, IsKey: false, IsMandatory: false),
        },
        Array.Empty<ODataNavigationProperty>());

    private static ODataPayloadBuildResult BuildBlank(string property, bool include, bool blankIncludedMeansNull) =>
        ODataPayloadBuilder.BuildPayloadJson(
            NullabilityEntity,
            new[] { new ODataFieldValue(property, include, Value: "   ") },
            enforceMandatory: false,
            blankIncludedMeansNull: blankIncludedMeansNull);

    [Fact]
    public void A_blank_included_field_is_omitted_unless_clear_semantics_are_requested()
    {
        // POST semantics, and the default — the service applies its own default for an absent property, so
        // omitting a blank is the right reading there and must stay unchanged.
        var result = BuildBlank("Nullable", include: true, blankIncludedMeansNull: false);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.False(doc.RootElement.TryGetProperty("Nullable", out _));
    }

    [Fact]
    public void A_blank_included_nullable_field_is_cleared_with_an_explicit_null()
    {
        // The whole point of #158: this used to be dropped, so a PATCH that emptied the only included field
        // sent "{}", F&O answered 204, and the green badge told the user a field had been cleared that
        // nothing had touched. The null is now in the body — and therefore in the payload preview.
        var result = BuildBlank("Nullable", include: true, blankIncludedMeansNull: true);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Nullable").ValueKind);
    }

    [Fact]
    public void A_blank_included_non_nullable_field_is_an_issue_rather_than_a_silent_omission()
    {
        var result = BuildBlank("NotNullable", include: true, blankIncludedMeansNull: true);

        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("isn't nullable", StringComparison.Ordinal));
        Assert.Contains(result.Issues, i => i.Contains("NotNullable", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unincluded_blank_field_stays_omitted_under_clear_semantics()
    {
        // Unchecked is the "leave this field alone" half of the distinction — it must not become a null.
        var result = BuildBlank("Nullable", include: false, blankIncludedMeansNull: true);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.Empty(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public void A_property_the_caller_never_mentioned_is_not_nulled_under_clear_semantics()
    {
        // A mandatory property reaches the loop with include=true even when it has no ODataFieldValue at all
        // (prop.Mandatory stands in for the checkbox). Nulling that would be the builder inventing a clear
        // for a field the caller never asked about.
        var entity = new ODataEntity(
            "TestEntity",
            new[] { new ODataProperty("Key", "Edm.String", Nullable: true, IsKey: true, IsMandatory: true) },
            Array.Empty<ODataNavigationProperty>());

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, Array.Empty<ODataFieldValue>(),
            enforceMandatory: false, blankIncludedMeansNull: true);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.False(doc.RootElement.TryGetProperty("Key", out _));
    }

    [Fact]
    public void The_literal_text_null_still_maps_to_json_null_under_both_readings()
    {
        foreach (var clearSemantics in new[] { false, true })
        {
            var result = ODataPayloadBuilder.BuildPayloadJson(
                NullabilityEntity,
                new[] { new ODataFieldValue("Nullable", Include: true, Value: "null") },
                enforceMandatory: false,
                blankIncludedMeansNull: clearSemantics);

            Assert.True(result.Ok, string.Join("; ", result.Issues));
            using var doc = JsonDocument.Parse(result.Json!);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Nullable").ValueKind);
        }
    }

    [Fact]
    public void A_mandatory_blank_is_still_reported_as_mandatory_not_as_a_clear()
    {
        // Mandatory enforcement runs first, so a POST-style build gets the message that names the real
        // problem instead of the nullability advice.
        var entity = new ODataEntity(
            "TestEntity",
            new[] { new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true) },
            Array.Empty<ODataNavigationProperty>());

        var result = ODataPayloadBuilder.BuildPayloadJson(entity,
            new[] { new ODataFieldValue("Name", Include: true, Value: string.Empty) },
            enforceMandatory: true, blankIncludedMeansNull: true);

        Assert.False(result.Ok);
        Assert.Single(result.Issues);
        Assert.Contains("mandatory", result.Issues[0], StringComparison.OrdinalIgnoreCase);
    }

    // --- Culture-ambiguous input is rejected rather than silently reinterpreted (#156) ---

    [Fact]
    public void Decimal_rejects_a_comma_used_as_the_decimal_separator()
    {
        // "1,5" is one-and-a-half to most of the world. Group separators are accepted by .NET without any
        // position validation, so NumberStyles.Number read this as 15 — no issue raised, 15 written to F&O.
        var result = Coerce("Edm.Decimal", "1,5");

        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("thousands separators", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Decimal_rejects_grouped_thousands()
    {
        // Well-formed grouping is rejected too: accepting it is what makes "1,5" indistinguishable from a
        // mis-grouped number, and F&O has no use for a separator the builder would have to strip anyway.
        Assert.False(Coerce("Edm.Decimal", "1,234.56").Ok);
    }

    [Fact]
    public void Decimal_accepts_an_invariant_number_with_a_decimal_point()
    {
        Assert.Equal(1.234m, Coerced("Edm.Decimal", "1.234").GetDecimal());
    }

    [Fact]
    public void Double_keeps_exponent_notation_but_drops_thousands_separators()
    {
        Assert.Equal(1000d, Coerced("Edm.Double", "1e3").GetDouble());
        Assert.False(Coerce("Edm.Double", "1,5").Ok);
    }

    [Fact]
    public void Single_rejects_thousands_separators()
    {
        Assert.False(Coerce("Edm.Single", "1,5").Ok);
    }

    [Fact]
    public void Int16_is_range_checked_instead_of_being_validated_as_an_Int32()
    {
        // Folded into the Int32 case, "40000" passed here and was rejected by the service instead — a much
        // worse place to discover the range.
        var result = Coerce("Edm.Int16", "40000");

        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("16-bit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal((short)32000, Coerced("Edm.Int16", "32000").GetInt16());
    }

    [Fact]
    public void DateTimeOffset_rejects_a_locale_short_date()
    {
        // "11/08/2026" is a *valid* InvariantCulture parse (8 November), so the NZ user who meant 11 August
        // got no error at all — just a different date.
        var result = Coerce("Edm.DateTimeOffset", "11/08/2026");

        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("ISO 8601", StringComparison.Ordinal));
    }

    [Fact]
    public void DateTimeOffset_reads_an_offsetless_input_as_utc_on_every_machine()
    {
        var emitted = DateTimeOffset.Parse(
            Coerced("Edm.DateTimeOffset", "2026-08-11").GetString()!, CultureInfo.InvariantCulture);

        // Not the host timezone's midnight: identical keystrokes must produce one instant everywhere.
        Assert.Equal(TimeSpan.Zero, emitted.Offset);
        Assert.Equal(new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc), emitted.UtcDateTime);
    }

    [Fact]
    public void DateTimeOffset_applies_an_explicit_offset()
    {
        var emitted = DateTimeOffset.Parse(
            Coerced("Edm.DateTimeOffset", "2026-08-11T14:30:00+12:00").GetString()!, CultureInfo.InvariantCulture);

        // 14:30 at +12:00 is 02:30Z — the offset was applied, not dropped and not re-read as local time.
        Assert.Equal(new DateTime(2026, 8, 11, 2, 30, 0, DateTimeKind.Utc), emitted.UtcDateTime);
    }

    [Fact]
    public void DateTimeOffset_accepts_the_iso_forms_the_message_advertises()
    {
        Assert.True(Coerce("Edm.DateTimeOffset", "2026-08-11T14:30").Ok);
        Assert.True(Coerce("Edm.DateTimeOffset", "2026-08-11T14:30:00Z").Ok);
        Assert.True(Coerce("Edm.DateTimeOffset", "2026-08-11T14:30:00.1234567Z").Ok);
    }

    [Fact]
    public void Date_accepts_only_yyyy_MM_dd()
    {
        Assert.Equal("2026-08-11", Coerced("Edm.Date", "2026-08-11").GetString());

        // DateOnly.TryParse is a *free* parse even under InvariantCulture: it read "11/08/2026" as
        // 8 November and "1,5" as 5 January of the current year. Harmless while Edm.Date was unreachable
        // from the app; a live instance of #156 the moment it became reachable.
        Assert.False(Coerce("Edm.Date", "11/08/2026").Ok);
        Assert.False(Coerce("Edm.Date", "1,5").Ok);
    }
}
