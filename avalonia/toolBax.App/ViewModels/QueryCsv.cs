using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ToolBax.App.ViewModels;

/// <summary>Builds RFC-4180-ish CSV from the Query Builder result columns + rows.</summary>
public static class QueryCsv
{
    /// <summary>
    /// The em-dash the result grid shows for a null or absent cell. It is a <em>display</em> affordance:
    /// a CSV consumer wants an empty field, and exporting the literal character also made Excel's ANSI
    /// mis-decode of a BOM-less file visible as <c>â€"</c> on every null (#168). Declared here so the
    /// grid's producer (<c>QueryBuilderViewModel.CellText</c>) and this exporter agree on one literal.
    /// </summary>
    public const string NullPlaceholder = "—";

    public static string Build(IReadOnlyList<string> columns, IEnumerable<QueryResultRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(Escape)));
        foreach (var row in rows)
        {
            sb.Append("\r\n"); // RFC 4180 record terminator
            sb.Append(string.Join(",", columns.Select(c => Escape(Exported(row[c])))));
        }

        return sb.ToString();
    }

    // The grid's null placeholder is not data — export it as an empty field. Escape then leaves it
    // completely alone: an empty string trips neither the formula guard (which needs a leading
    // character) nor a quote trigger, so it lands as a bare, unquoted empty CSV field.
    private static string Exported(string cell) => cell == NullPlaceholder ? string.Empty : cell;

    // Characters that make a spreadsheet treat a leading cell as a formula (CSV/formula injection).
    private static readonly char[] FormulaLeaders = { '=', '+', '-', '@', '\t', '\r' };
    private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };

    // Quote fields containing a comma/quote/newline (doubling embedded quotes), and neutralise
    // formula-injection by prefixing a leading =,+,-,@,TAB,CR with an apostrophe so the value is
    // treated as literal text when the CSV is opened in Excel/Sheets.
    private static string Escape(string value)
    {
        var needsFormulaGuard = value.Length > 0 && Array.IndexOf(FormulaLeaders, value[0]) >= 0;
        if (!needsFormulaGuard && value.IndexOfAny(QuoteTriggers) < 0)
        {
            return value;
        }

        var body = needsFormulaGuard ? "'" + value : value;
        return $"\"{body.Replace("\"", "\"\"")}\"";
    }
}
