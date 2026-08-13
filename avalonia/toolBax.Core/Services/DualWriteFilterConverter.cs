using System.Text;

namespace ToolBax.Core.Services;

/// <summary>
/// Translates a dual-write map leg's X++ <c>sourceFilter</c> into an OData <c>$filter</c> expression
/// (used to preview / validate the synced row set). Operators outside string literals are translated
/// (<c>==</c>→<c>eq</c>, <c>&amp;&amp;</c>→<c>and</c>, a bare <c>!</c>→<c>not</c>, …); string literals
/// (delimited by <c>"</c> or <c>'</c>) are emitted as OData single-quoted literals with their contents
/// left verbatim — <em>including</em> internal whitespace — and embedded single quotes doubled.
/// X++ enum tokens (<c>NoYes::Yes</c>) are rewritten to qualified OData enum literals, which needs no
/// metadata; field-name normalisation does need the F&amp;O catalogue and lives in
/// <see cref="FoFilterFieldCaser"/>, applied where the entity is known.
/// </summary>
public static class DualWriteFilterConverter
{
    /// <summary>
    /// The namespace F&amp;O projects its data-entity enums under, so an X++ <c>NoYes::Yes</c> becomes the
    /// OData enum literal <c>Microsoft.Dynamics.DataEntities.NoYes'Yes'</c>. Proven live against a real
    /// F&amp;O environment (issue #204's matrix, RicohDev 2026-08-13): that qualified form is the only one
    /// the OData layer accepts — the verbatim <c>NoYes::Yes</c> is a parse error ("')' or operator expected")
    /// and a quoted <c>'Yes'</c> is an "incompatible types … and 'Edm.String'" error. The assumption it
    /// rests on is that a data entity's enums live in this namespace, which is where F&amp;O puts the enums
    /// its data entities expose; an enum from anywhere else fails loudly at the server rather than silently.
    /// </summary>
    public const string FoDataEntitiesNamespace = "Microsoft.Dynamics.DataEntities";

    public static string XppToOData(string? xppFilter)
    {
        if (string.IsNullOrWhiteSpace(xppFilter))
        {
            return string.Empty;
        }

        var source = xppFilter.Trim();
        var output = new StringBuilder(source.Length * 2);
        char? stringDelim = null; // the active string-literal delimiter (" or '), or null when outside

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (stringDelim is not null)
            {
                if (ch == stringDelim)
                {
                    output.Append('\''); // close as an OData single-quoted literal
                    stringDelim = null;
                }
                else
                {
                    output.Append(ch == '\'' ? "''" : ch.ToString()); // double embedded single quotes
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                stringDelim = ch;
                output.Append('\''); // open as an OData single-quoted literal
                continue;
            }

            // A word (identifier or number) is consumed whole so an X++ enum token can be recognised HERE,
            // inside the literal-aware walk — a later regex pass over the finished string couldn't tell a
            // "NoYes::Yes" in the expression from one sitting inside a quoted literal (#162).
            if (IsWordChar(ch))
            {
                var wordEnd = i;
                while (wordEnd + 1 < source.Length && IsWordChar(source[wordEnd + 1]))
                {
                    wordEnd++;
                }

                var word = source[i..(wordEnd + 1)];
                i = wordEnd;

                // Only identifier::identifier is an enum token. Anything else — a leading digit, a lone
                // colon, a missing or non-identifier member — is emitted exactly as written, so it fails
                // loudly at the server instead of being silently mangled into a different filter.
                if (IsIdentifier(word) && TryReadEnumMember(source, wordEnd + 1, out var member, out var afterMember))
                {
                    output.Append(FoDataEntitiesNamespace).Append('.').Append(word)
                        .Append('\'').Append(member).Append('\''); // member casing preserved as written
                    i = afterMember - 1;
                }
                else
                {
                    output.Append(word);
                }

                continue;
            }

            if (ch == '&' && i + 1 < source.Length && source[i + 1] == '&')
            {
                output.Append(" and ");
                i++;
                continue;
            }

            if (ch == '|' && i + 1 < source.Length && source[i + 1] == '|')
            {
                output.Append(" or ");
                i++;
                continue;
            }

            if (ch == '=' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" eq ");
                i++;
                continue;
            }

            if (ch == '=')
            {
                output.Append(" eq ");
                continue;
            }

            if (ch == '!' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ne ");
                i++;
                continue;
            }

            // Any remaining '!' outside a literal is X++'s unary logical not — '!=' was consumed above and
            // X++ has no other '!'-prefixed operator. OData requires whitespace after 'not', which the
            // padding here (plus the collapse pass) guarantees: "!(A == 1)" → "not (A eq 1)".
            if (ch == '!')
            {
                output.Append(" not ");
                continue;
            }

            if (ch == '>' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ge ");
                i++;
                continue;
            }

            if (ch == '<' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" le ");
                i++;
                continue;
            }

            if (ch == '>')
            {
                output.Append(" gt ");
                continue;
            }

            if (ch == '<')
            {
                output.Append(" lt ");
                continue;
            }

            output.Append(ch);
        }

        // Collapse the whitespace runs left by operator padding (and by source newlines/tabs) — but only
        // outside string literals, whose contents must survive byte-for-byte.
        return CollapseWhitespaceOutsideLiterals(output.ToString());
    }

    /// <summary>A character that can appear inside an X++ identifier or number literal.</summary>
    private static bool IsWordChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

    /// <summary>True for an X++ identifier: <c>[A-Za-z_][A-Za-z0-9_]*</c>.</summary>
    private static bool IsIdentifier(string word) =>
        word.Length > 0 && (char.IsAsciiLetter(word[0]) || word[0] == '_');

    /// <summary>
    /// Reads the <c>::Member</c> half of an X++ enum token starting at <paramref name="index"/>. Succeeds
    /// only for <c>::</c> followed by an identifier; <paramref name="afterMember"/> is then the index just
    /// past the member.
    /// </summary>
    private static bool TryReadEnumMember(string source, int index, out string member, out int afterMember)
    {
        member = string.Empty;
        afterMember = index;

        // index + 2 must be a real position: "::" plus at least one member character.
        if (index + 2 >= source.Length || source[index] != ':' || source[index + 1] != ':')
        {
            return false;
        }

        var start = index + 2;
        if (!char.IsAsciiLetter(source[start]) && source[start] != '_')
        {
            return false;
        }

        var end = start;
        while (end + 1 < source.Length && IsWordChar(source[end + 1]))
        {
            end++;
        }

        member = source[start..(end + 1)];
        afterMember = end + 1;
        return true;
    }

    /// <summary>
    /// Collapses each run of whitespace <em>outside</em> an OData single-quoted literal to a single space,
    /// dropping leading/trailing runs entirely, and passes literal spans through unchanged. Tracks the
    /// literal state as OData defines it: <c>''</c> inside a literal is an escaped quote, not the end of
    /// the literal. An unterminated literal (malformed input) passes through verbatim to its end.
    /// </summary>
    private static string CollapseWhitespaceOutsideLiterals(string converted)
    {
        var result = new StringBuilder(converted.Length);
        var inLiteral = false;

        for (var i = 0; i < converted.Length; i++)
        {
            var ch = converted[i];

            if (inLiteral)
            {
                result.Append(ch);

                if (ch != '\'')
                {
                    continue; // literal content — verbatim, whitespace and all
                }

                if (i + 1 < converted.Length && converted[i + 1] == '\'')
                {
                    result.Append('\''); // an escaped quote: the literal continues
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
                result.Append(ch);
                inLiteral = true;
                continue;
            }

            if (!char.IsWhiteSpace(ch))
            {
                result.Append(ch);
                continue;
            }

            while (i + 1 < converted.Length && char.IsWhiteSpace(converted[i + 1]))
            {
                i++; // swallow the rest of the run
            }

            // An interior run becomes exactly one space; a leading or trailing run becomes nothing.
            if (result.Length > 0 && i + 1 < converted.Length)
            {
                result.Append(' ');
            }
        }

        return result.ToString();
    }
}
