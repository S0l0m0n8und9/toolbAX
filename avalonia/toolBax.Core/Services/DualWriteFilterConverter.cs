using System.Text;
using System.Text.RegularExpressions;

namespace ToolBax.Core.Services;

/// <summary>
/// Translates a dual-write map leg's X++ <c>sourceFilter</c> into an OData <c>$filter</c> expression
/// (used to preview / validate the synced row set). A faithful port of the WPF plugin's lexer: operators
/// outside string literals are translated (<c>==</c>→<c>eq</c>, <c>&amp;&amp;</c>→<c>and</c>, …), double
/// quotes become OData single quotes, and embedded single quotes are doubled. Enum-token and field-name
/// normalisation (which need the F&amp;O catalogue) are not handled here.
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
        var inString = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (ch == '"')
            {
                inString = !inString;
                output.Append('\'');
                continue;
            }

            if (inString)
            {
                output.Append(ch == '\'' ? "''" : ch.ToString());
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

            if (ch is '\r' or '\n' or '\t')
            {
                output.Append(' ');
                continue;
            }

            output.Append(ch);
        }

        return Regex.Replace(output.ToString(), @"\s+", " ").Trim();
    }
}
