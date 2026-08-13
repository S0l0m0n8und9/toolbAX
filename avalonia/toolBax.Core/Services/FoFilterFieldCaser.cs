using System;
using System.Collections.Generic;
using System.Text;

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
/// references, and re-casing or reporting one of them would break a form that is live-proven good.
/// </para>
/// <para>
/// <b>Known limit (#204):</b> a numeric literal compared against an enum property — e.g.
/// <c>AssociatedContactType eq 0</c> — still fails server-side ("incompatible types … and 'Edm.Int32'"),
/// and correcting the field's casing doesn't change that. Typing the literal needs per-property metadata
/// (which property is an enum, and which member each ordinal is) that this seam deliberately doesn't have:
/// it validates names, not types. Such a filter is therefore passed through and the server's own message
/// is the honest failure.
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

    /// <summary>
    /// Rewrites each bare identifier in <paramref name="odataFilter"/> that case-insensitively matches one
    /// of <paramref name="propertyNames"/> to that property's exact casing, and collects the identifiers
    /// that match none of them. With no property names to check against, the filter comes back unchanged
    /// and nothing is reported — "nothing to validate against" is not the same as "every field is wrong".
    /// </summary>
    public static FoFilterCasing Correct(string? odataFilter, IReadOnlyList<string>? propertyNames)
    {
        var source = odataFilter ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source) || propertyNames is null || propertyNames.Count == 0)
        {
            return new FoFilterCasing(source, Array.Empty<string>());
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in propertyNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                properties.TryAdd(name, name); // first spelling wins if an entity lists a name twice
            }
        }

        var output = new StringBuilder(source.Length);
        var unknown = new List<string>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inLiteral = false;

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
                output.Append(ch);
                inLiteral = true;
                continue;
            }

            if (!IsWordChar(ch))
            {
                output.Append(ch);
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

            if (!IsFieldReference(word, before, after))
            {
                output.Append(word); // a number, keyword, enum/namespace token, function or path segment
                continue;
            }

            if (properties.TryGetValue(word, out var exact))
            {
                output.Append(exact); // the entity's own spelling, which is the only one F&O answers to
                continue;
            }

            output.Append(word);
            if (reported.Add(word))
            {
                unknown.Add(word); // reported as written, so the user can find it in the map's filter
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
}
