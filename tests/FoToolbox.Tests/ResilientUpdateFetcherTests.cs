using FoToolbox.Updater;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class ResilientUpdateFetcherTests
{
    private sealed class FlakyFetcher : IUpdateFetcher
    {
        private readonly int _failures;
        private int _attempts;
        public FlakyFetcher(int failures) => _failures = failures;

        public Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            _attempts++;
            if (_attempts <= _failures)
            {
                throw new IOException("fail");
            }
            return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 }));
        }
    }

    [Fact]
    public async Task Retries_Then_Succeeds()
    {
        var fetcher = new ResilientUpdateFetcher(new FlakyFetcher(1), maxRetries: 2, baseDelay: TimeSpan.FromMilliseconds(1));
        var stream = await fetcher.FetchAsync(new Uri("http://test"));
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task Exceeds_Retries()
    {
        var fetcher = new ResilientUpdateFetcher(new FlakyFetcher(5), maxRetries: 1, baseDelay: TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<IOException>(() => fetcher.FetchAsync(new Uri("http://test")));
    }
}
