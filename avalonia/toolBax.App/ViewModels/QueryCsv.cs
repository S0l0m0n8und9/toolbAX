using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            sb.Append("\r\n"); // RFC 4180 record terminator
            // A null or absent cell becomes an empty field. Nullness is read from the row model
            // (QueryResultRow.Raw), NOT inferred by matching the grid's em-dash placeholder — that
            // comparison silently blanked a genuine "—" value, which is data (PR #193 review).
            // Escape then leaves an empty string completely alone: it trips neither the formula guard
            // (which needs a leading character) nor a quote trigger.
            sb.Append(string.Join(",", columns.Select(c => Escape(row.Raw(c) ?? string.Empty))));
        }

        return sb.ToString();
    }

    public static async Task WriteAsync(Stream output, IReadOnlyList<string> columns,
        IAsyncEnumerable<IReadOnlyDictionary<string, string>> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("The output stream must be writable.", nameof(output));

        await using var writer = new StreamWriter(output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteAsync(string.Join(",", columns.Select(Escape)).AsMemory(), cancellationToken);
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("\r\n".AsMemory(), cancellationToken);
            var line = string.Join(",", columns.Select(column =>
                Escape(row.TryGetValue(column, out var value) ? value : string.Empty)));
            await writer.WriteAsync(line.AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
    }

    // Characters that make a spreadsheet treat a leading cell as a formula (CSV/formula injection).
    private static readonly char[] FormulaLeaders = { '=', '+', '-', '@', '\t', '\r' };
    private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };

    // Quote fields containing a comma/quote/newline (doubling embedded quotes), and neutralise
    // formula-injection by prefixing a leading =,+,-,@,TAB,CR with an apostrophe so the value is
    // treated as literal text when the CSV is opened in Excel/Sheets.
    // Deliberately identical to FoToolbox.Core.Export.CsvExporter.Escape — the two CSV writers must
    // agree on escaping, so change them together.
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
