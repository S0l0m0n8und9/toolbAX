using System.Text;

namespace ToolBax.Core.Services;

/// <summary>
/// Translates a dual-write map leg's X++ <c>sourceFilter</c> into an OData <c>$filter</c> expression
/// (used to preview / validate the synced row set). Operators outside string literals are translated
/// (<c>==</c>→<c>eq</c>, <c>&amp;&amp;</c>→<c>and</c>, a bare <c>!</c>→<c>not</c>, …); string literals
/// (delimited by <c>"</c> or <c>'</c>) are emitted as OData single-quoted literals with their contents
/// left verbatim — <em>including</em> internal whitespace — and embedded single quotes doubled.
/// Enum-token and field-name normalisation (which need the F&amp;O catalogue) are not handled here.
/// </summary>
public static class DualWriteFilterConverter
{
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
