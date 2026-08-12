using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Export;

/// <summary>
/// Exports OData pages to CSV with escaping, progress reporting, and cancellation support.
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// Writes every row of every page of <paramref name="request"/> to <paramref name="output"/> as CSV.
    /// </summary>
    /// <remarks>
    /// The header is the union of every row's keys, in first-seen order, because Dataverse/F&amp;O omit
    /// null properties: no single row (and no single page — the first page of a filtered query can come
    /// back empty) is a reliable column list, and a column missing from the row the header was taken from
    /// used to be dropped for every row. That union is only known once the last page has been read, so
    /// rows are buffered rather than written page-by-page; <paramref name="progress"/> still reports rows
    /// read as each page arrives, and nothing is written if the export is cancelled part-way (better than
    /// a truncated file whose header is missing columns).
    /// </remarks>
    public static async Task ExportAsync(IODataClient client, QueryRequest request, Stream output, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        var columns = new List<string>();
        // Case-insensitive to match the row dictionaries HttpODataClient builds, so "Name"/"name" across
        // pages produce one column that both rows can be read through.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var totalRows = 0;

        await foreach (var page in client.StreamAsync(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var row in page.Rows)
            {
                foreach (var key in row.Keys)
                {
                    if (known.Add(key))
                    {
                        columns.Add(key);
                    }
                }

                rows.Add(row);
            }

            totalRows += page.Rows.Count;
            progress?.Invoke(totalRows);
        }

        await writer.WriteLineAsync(string.Join(",", columns.Select(Escape)));

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(",", columns.Select(c => Escape(row.TryGetValue(c, out var v) ? v?.ToString() ?? string.Empty : string.Empty)));
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }

    public static async Task ExportTableAsync(DataTable table, Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        var cols = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        await writer.WriteLineAsync(string.Join(",", cols.Select(Escape)));
        foreach (DataRow row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(",", cols.Select(c => Escape(row[c]?.ToString() ?? string.Empty)));
            await writer.WriteLineAsync(line);
        }
        await writer.FlushAsync();
    }

    // Characters that make a spreadsheet treat a leading cell as a formula (CSV/formula injection).
    private static readonly char[] FormulaLeaders = { '=', '+', '-', '@', '\t', '\r' };
    private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };

    // Quote fields containing a comma/quote/newline (doubling embedded quotes), and neutralise
    // formula-injection by prefixing a leading =,+,-,@,TAB,CR with an apostrophe so the value is treated
    // as literal text when the CSV is opened in Excel/Sheets.
    // Deliberately identical to ToolBax.App.ViewModels.QueryCsv.Escape — the two CSV writers must agree
    // on escaping, so change them together.
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
