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
/// Streams OData pages to CSV with back-pressure, escaping, and cancellation support.
/// </summary>
public static class CsvExporter
{
    public static async Task ExportAsync(IODataClient client, QueryRequest request, Stream output, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        bool headerWritten = false;
        List<string> columns = new();
        var totalRows = 0;

        await foreach (var page in client.StreamAsync(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!headerWritten)
            {
                columns = page.Rows.FirstOrDefault()?.Keys.ToList() ?? new List<string>();
                await writer.WriteLineAsync(string.Join(",", columns.Select(Escape)));
                headerWritten = true;
            }

            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = string.Join(",", columns.Select(c => Escape(row.TryGetValue(c, out var v) ? v?.ToString() ?? string.Empty : string.Empty)));
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync(); // back-pressure: flush before fetching next page
            totalRows += page.Rows.Count;
            progress?.Invoke(totalRows);
        }
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

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
