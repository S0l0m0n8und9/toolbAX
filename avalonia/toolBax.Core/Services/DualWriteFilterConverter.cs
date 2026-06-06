using System;
using System.Text;

namespace ToolBax.Core.Services;

/// <summary>
/// Translates a dual-write map leg's X++ <c>sourceFilter</c> into an OData <c>$filter</c> expression
/// (used to preview / validate the synced row set). Operators outside string literals are translated
/// (<c>==</c>→<c>eq</c>, <c>&amp;&amp;</c>→<c>and</c>, …); string literals (delimited by <c>"</c> or
/// <c>'</c>) are emitted as OData single-quoted literals with their contents left verbatim and embedded
/// single quotes doubled. Enum-token and field-name normalisation (which need the F&amp;O catalogue) are
/// not handled here.
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

        // Collapse any whitespace runs (operator padding, source newlines/tabs) into single spaces.
        return string.Join(' ', output.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
