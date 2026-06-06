using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ToolBax.App.ViewModels;

/// <summary>Builds RFC-4180-ish CSV from the Query Builder result columns + rows.</summary>
public static class QueryCsv
{
    public static string Build(IReadOnlyList<string> columns, IEnumerable<QueryResultRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(Escape)));
        foreach (var row in rows)
        {
            sb.Append('\n');
            sb.Append(string.Join(",", columns.Select(c => Escape(row[c]))));
        }

        return sb.ToString();
    }

    // Quote fields containing a comma, quote, CR or LF; double any embedded quotes.
    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
