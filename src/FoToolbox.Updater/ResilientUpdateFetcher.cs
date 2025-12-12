using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

/// <summary>
/// Wraps an IUpdateFetcher with simple retry/backoff for transient failures.
/// </summary>
public sealed class ResilientUpdateFetcher : IUpdateFetcher
{
    private readonly IUpdateFetcher _inner;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    public ResilientUpdateFetcher(IUpdateFetcher inner, int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        _inner = inner;
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        int attempt = 0;
        Exception? last = null;

        while (attempt <= _maxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _inner.FetchAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                last = ex;
                attempt++;
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("Fetch failed with unknown error.");
    }
}
