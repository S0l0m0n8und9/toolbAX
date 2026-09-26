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
    /// used to be dropped for every row. That union is only known once the last page has been read, so each
    /// row's rendered strings are temporarily spooled to disk rather than retained in memory. The output
    /// stream is untouched until source spooling completes and cancellation is checked. Once final output
    /// writing begins, a generic stream failure or cancellation may leave partial bytes; callers that need
    /// destination atomicity must provide it. The caller-owned output stream remains open.
    /// </remarks>
    public static async Task ExportAsync(IODataClient client, QueryRequest request, Stream output,
        Action<int>? progress = null, CancellationToken cancellationToken = default) =>
        await ExportCoreAsync(client, request, output, Path.GetTempPath(), progress, cancellationToken).ConfigureAwait(false);

    internal static Task ExportAsyncForTest(IODataClient client, QueryRequest request, Stream output,
        string spoolDirectory, Action<int>? progress = null, CancellationToken cancellationToken = default) =>
        ExportCoreAsync(client, request, output, spoolDirectory, progress, cancellationToken);

    private static async Task ExportCoreAsync(IODataClient client, QueryRequest request, Stream output,
        string spoolDirectory, Action<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var spool = CsvRowSpool.Create(spoolDirectory);
        var columns = new List<string>();
        var canonicalColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;

        await foreach (var page in client.StreamAsync(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in row)
                {
                    if (!canonicalColumns.TryGetValue(cell.Key, out var canonical))
                    {
                        canonical = cell.Key;
                        canonicalColumns.Add(cell.Key, canonical);
                        columns.Add(canonical);
                    }

                    rendered[canonical] = cell.Value?.ToString() ?? string.Empty;
                }

                await spool.AppendAsync(rendered, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                totalRows++;
            }

            progress?.Invoke(totalRows);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await spool.CompleteWritingAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await using var writer = new StreamWriter(output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        var header = string.Join(",", columns.Select(Escape));
        await writer.WriteLineAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);

        await foreach (var row in spool.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(",", columns.Select(c => Escape(row.TryGetValue(c, out var value) ? value : string.Empty)));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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
