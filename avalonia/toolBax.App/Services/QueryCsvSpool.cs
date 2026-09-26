using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Export;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Services;

/// <summary>Owns Query Export All's rendered rows and completed CSV temporary streams.</summary>
internal sealed class QueryCsvSpool : IAsyncDisposable
{
    private readonly string _directory;
    private readonly CsvRowSpool _rows;
    private FileStream? _csv;

    private QueryCsvSpool(string directory)
    {
        _directory = directory;
        _rows = CsvRowSpool.Create(directory, StringComparer.Ordinal);
    }

    internal static QueryCsvSpool Create(string? directory = null) =>
        new(directory ?? Path.GetTempPath());

    internal async ValueTask AppendAsync(QueryResultRow row, IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
    {
        var rendered = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
        foreach (var column in columns)
        {
            rendered[column] = row.Raw(column) ?? string.Empty;
        }
        await _rows.AppendAsync(rendered, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Stream> CompleteAsync(IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
    {
        if (_csv is not null) throw new InvalidOperationException("The Query CSV spool is already complete.");
        cancellationToken.ThrowIfCancellationRequested();
        await _rows.CompleteWritingAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var csv = CreateTemporaryCsv(_directory);
        try
        {
            await QueryCsv.WriteAsync(csv, columns, _rows.ReadRowsAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            csv.Position = 0;
            _csv = csv;
            return csv;
        }
        catch
        {
            await csv.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_csv is not null)
            {
                await _csv.DisposeAsync().ConfigureAwait(false);
                _csv = null;
            }
        }
        finally
        {
            await _rows.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FileStream CreateTemporaryCsv(string directory)
    {
        var path = Path.Combine(directory, $".toolbax-query-{Guid.NewGuid():N}.csv");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 4096,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new FileStream(path, options);
    }
}
