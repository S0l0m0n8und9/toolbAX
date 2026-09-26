using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Export;

/// <summary>Private per-export storage for rendered row values while the final CSV header is discovered.</summary>
internal sealed class JsonLineRowSpool : IAsyncDisposable
{
    private readonly FileStream _stream;
    private StreamWriter? _writer;
    private bool _readyToRead;

    private JsonLineRowSpool(FileStream stream)
    {
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true);
    }

    public static JsonLineRowSpool Create(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(directory, $".toolbax-csv-{Guid.NewGuid():N}.jsonl");
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

        return new JsonLineRowSpool(new FileStream(path, options));
    }

    public async ValueTask AppendAsync(IReadOnlyDictionary<string, string> row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = _writer ?? throw new InvalidOperationException("The row spool is no longer writable.");
        var json = JsonSerializer.Serialize(row);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask CompleteWritingAsync(CancellationToken cancellationToken)
    {
        if (_readyToRead) return;
        cancellationToken.ThrowIfCancellationRequested();
        var writer = _writer ?? throw new InvalidOperationException("The row spool is no longer writable.");
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
        _stream.Position = 0;
        _readyToRead = true;
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_readyToRead) throw new InvalidOperationException("The row spool has not finished writing.");
        using var reader = new StreamReader(_stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (line is null) yield break;

            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(line)
                ?? throw new InvalidDataException("The temporary export row was invalid.");
            yield return new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_writer is not null)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }
        }
        finally
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
