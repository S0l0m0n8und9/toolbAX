using System;
using System.Collections.Generic;
using System.Text;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// The outcome of <see cref="FoFilterFieldCaser.Correct"/>: the filter with every recognised field name
/// spelled as the entity really spells it, plus the identifiers that matched no property at all (the ones
/// that would make F&amp;O answer "Could not find a property named …").
/// </summary>
public sealed record FoFilterCasing(string Filter, IReadOnlyList<string> UnknownFields);

/// <summary>
/// Reconciles an OData <c>$filter</c> (as produced by <see cref="DualWriteFilterConverter"/> from a
/// dual-write leg's X++ <c>sourceFilter</c>) with an F&amp;O entity's real property names. X++ source
/// filters carry staging-case identifiers (<c>ISONETIMECUSTOMER</c>) while F&amp;O's OData property lookup
/// is case-sensitive PascalCase (<c>IsOneTimeCustomer</c>), so an uncorrected filter is a 400 for every
/// such leg — proven live in issue #204's matrix.
/// <para>
/// Walks the expression with the same literal discipline as the converter (#162): quoted literals are
/// passed through untouched, and so are the tokens of an enum literal
/// (<c>Microsoft.Dynamics.DataEntities.NoYes'Yes'</c>) — its namespace, type name and member are not field
/// references, and re-casing or reporting one of them would break a form that is live-proven good. In that
/// same single literal-aware pass, the walk now ALSO types a quoted literal that names one of an enum
/// property's members — upgrading it to the qualified enum literal form that #204 proved live — rather
/// than running a second tokenizer over the filter to do that job separately (#207).
/// </para>
/// <para>
/// <b>Known limit (#204, #207):</b> a NUMERIC literal compared against an enum property — e.g.
/// <c>AssociatedContactType eq 0</c>, or <c>TransferOrderStatus ne 2</c> (RicohDev, 2026-08-13) — still
/// fails server-side ("incompatible types … and 'Edm.Int32'"), and correcting the field's casing doesn't
/// change that. Typing an ordinal would need an ordinal→member mapping that this seam deliberately doesn't
/// have. A quoted member string against an enum property — e.g. <c>LineStatus ne 'None'</c> (RicohDev,
/// 2026-08-13, #207's other live leg) — IS now upgraded, so the remaining limit is ordinals only: such a
/// filter is passed through and the server's own message is the honest failure.
/// </para>
/// </summary>
public static class FoFilterFieldCaser
{
    /// <summary>
    /// The bare words a converted filter can legitimately contain that are not field references: the
    /// operators <see cref="DualWriteFilterConverter"/> emits plus the literals it passes through.
    /// </summary>
    private static readonly HashSet<string> ODataKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "ne", "eq", "gt", "lt", "ge", "le", "true", "false", "null",
    };

    /// <summary>The comparison operators after which a quoted literal is a candidate for enum-typing.</summary>
    private static readonly HashSet<string> ComparisonOperators =
        new(StringComparer.OrdinalIgnoreCase) { "eq", "ne", "gt", "lt", "ge", "le" };

    /// <summary>
    /// Rewrites each bare identifier in <paramref name="odataFilter"/> that case-insensitively matches one
    /// of <paramref name="properties"/> to that property's exact casing, and collects the identifiers that
    /// match none of them. In the same walk, upgrades a quoted string literal compared (via <c>eq ne gt lt
    /// ge le</c>) against an enum-typed property to the qualified enum literal F&amp;O requires, when the
    /// literal case-insensitively names one of that property's members as reported by
    /// <paramref name="enumMembers"/>. A literal matching no member, an enum property with no qualified
    /// type, or a null/empty members lookup all pass the literal through unchanged — never fabricating a
    /// type reference. With no properties to check against, the filter comes back unchanged and nothing is
    /// reported — "nothing to validate against" is not the same as "every field is wrong".
    /// </summary>
    public static FoFilterCasing Correct(
        string? odataFilter,
        IReadOnlyList<EntityField>? properties,
        Func<string, IReadOnlyList<string>?>? enumMembers = null)
    {
        var source = odataFilter ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source) || properties is null || properties.Count == 0)
        {
            return new FoFilterCasing(source, Array.Empty<string>());
        }

        var byName = new Dictionary<string, EntityField>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in properties)
        {
            if (!string.IsNullOrWhiteSpace(field.Name))
            {
                byName.TryAdd(field.Name, field); // first spelling wins if an entity lists a name twice
            }
        }

        var output = new StringBuilder(source.Length);
        var unknown = new List<string>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inLiteral = false;
        EntityField? enumCandidate = null; // enum-typed property a following comparison operator would apply to
        var opSeen = false;                // ... and that operator has been seen

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (inLiteral)
            {
                output.Append(ch);

                if (ch != '\'')
                {
                    continue; // literal content — verbatim, identifiers and all
                }

                if (i + 1 < source.Length && source[i + 1] == '\'')
                {
                    output.Append('\''); // an escaped quote: the literal continues
                    i++;
                }
                else
                {
                    inLiteral = false; // the literal closed here
                }

                continue;
            }

            if (ch == '\'')
            {
                if (enumCandidate is not null && opSeen &&
                    TryTypeEnumLiteral(source, i, enumCandidate, enumMembers, out var replacement, out var afterLiteral))
                {
                    output.Append(replacement);
                    i = afterLiteral - 1; // the loop's i++ lands just past the literal
                    enumCandidate = null;
                    opSeen = false;
                    continue;
                }

                output.Append(ch);
                inLiteral = true;
                enumCandidate = null;
                opSeen = false;
                continue;
            }

            if (!IsWordChar(ch))
            {
                output.Append(ch);
                if (!char.IsWhiteSpace(ch))
                {
                    // Whitespace between a field, its operator and the literal must not clear the pending
                    // state; anything else (parens, commas, the rest of the expression) means an operator
                    // here would no longer be leading straight to that property's own literal.
                    enumCandidate = null;
                    opSeen = false;
                }

                continue;
            }

            var start = i;
            while (i + 1 < source.Length && IsWordChar(source[i + 1]))
            {
                i++;
            }

            var word = source[start..(i + 1)];
            var before = start > 0 ? source[start - 1] : '\0';
            var after = i + 1 < source.Length ? source[i + 1] : '\0';
            var isFieldReference = IsFieldReference(word, before, after);

            EntityField? resolved = null;
            if (isFieldReference && byName.TryGetValue(word, out var property))
            {
                resolved = property;
            }

            if (!isFieldReference)
            {
                output.Append(word); // a number, keyword, enum/namespace token, function or path segment
            }
            else if (resolved is not null)
            {
                output.Append(resolved.Name); // the entity's own spelling, which is the only one F&O answers to
            }
            else
            {
                output.Append(word);
                if (reported.Add(word))
                {
                    unknown.Add(word); // reported as written, so the user can find it in the map's filter
                }
            }

            if (resolved is not null)
            {
                enumCandidate = IsEnumProperty(resolved) ? resolved : null;
                opSeen = false;
            }
            else if (enumCandidate is not null && ComparisonOperators.Contains(word))
            {
                opSeen = true;
            }
            else
            {
                enumCandidate = null;
                opSeen = false;
            }
        }

        return new FoFilterCasing(output.ToString(), unknown);
    }

    /// <summary>
    /// True when <paramref name="word"/> is a bare field reference — i.e. something whose casing should be
    /// corrected, and whose absence from the entity is a real defect. Excluded: numbers, OData keywords,
    /// the type/namespace tokens of an enum literal (<c>Type'Member'</c>, qualified or not), function names
    /// (<c>contains(</c>), and any segment of a dotted or slashed path, which can't be checked against one
    /// entity's property list and so is left for the server to judge.
    /// </summary>
    private static bool IsFieldReference(string word, char before, char after) =>
        (char.IsAsciiLetter(word[0]) || word[0] == '_')
        && !ODataKeywords.Contains(word)
        && after is not ('\'' or '(' or '.' or '/')
        && before is not ('.' or '/');

    private static bool IsWordChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

    /// <summary>
    /// True when <paramref name="field"/> is enum-typed AND carries everything needed to build a qualified
    /// enum literal for it. A blank <see cref="EntityField.EnumType"/> or
    /// <see cref="EntityField.QualifiedEnumType"/> means never — fabricating a type reference is worse than
    /// leaving the literal for the server to reject (same reasoning as <c>QueryFilter.FormatQuoted</c>).
    /// </summary>
    private static bool IsEnumProperty(EntityField field) =>
        field is { Type: "Enum" } && !string.IsNullOrWhiteSpace(field.QualifiedEnumType)
        && !string.IsNullOrWhiteSpace(field.EnumType);

    /// <summary>
    /// Reads the OData literal opening at <paramref name="quoteIndex"/>: <paramref name="content"/> with
    /// <c>''</c> unescaped to <c>'</c>, <paramref name="afterLiteral"/> the index just past the closing
    /// quote. False for an unterminated literal — malformed input then keeps today's verbatim path.
    /// </summary>
    private static bool TryReadLiteral(string source, int quoteIndex, out string content, out int afterLiteral)
    {
        var sb = new StringBuilder();
        var i = quoteIndex + 1;
        while (i < source.Length)
        {
            if (source[i] == '\'')
            {
                if (i + 1 < source.Length && source[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }

                content = sb.ToString();
                afterLiteral = i + 1;
                return true;
            }

            sb.Append(source[i]);
            i++;
        }

        content = string.Empty;
        afterLiteral = source.Length;
        return false;
    }

    /// <summary>
    /// True when the literal at <paramref name="quoteIndex"/> names one of <paramref name="property"/>'s
    /// enum members; <paramref name="replacement"/> is then the qualified enum literal spelled with the
    /// members list's own casing (so the member's own spelling wins over the filter's).
    /// </summary>
    private static bool TryTypeEnumLiteral(string source, int quoteIndex, EntityField property,
        Func<string, IReadOnlyList<string>?>? enumMembers, out string replacement, out int afterLiteral)
    {
        replacement = string.Empty;
        afterLiteral = quoteIndex;

        if (enumMembers is null || !TryReadLiteral(source, quoteIndex, out var content, out afterLiteral))
        {
            return false;
        }

        var members = enumMembers(property.EnumType!);
        if (members is not null)
        {
            foreach (var member in members)
            {
                if (string.Equals(member, content, StringComparison.OrdinalIgnoreCase))
                {
                    replacement = $"{property.QualifiedEnumType}'{member}'";
                    return true;
                }
            }
        }

        return false;
    }
}
