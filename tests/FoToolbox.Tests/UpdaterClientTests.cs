using FoToolbox.Updater;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class UpdaterClientTests
{
    private sealed class FakeFetcher : IUpdateFetcher
    {
        private readonly byte[] _payload;
        public FakeFetcher(byte[] payload) => _payload = payload;

        public Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Stream s = new MemoryStream(_payload);
            return Task.FromResult(s);
        }
    }

    [Fact]
    public async Task DownloadAndStage_Validates_Hash()
    {
        var data = Encoding.UTF8.GetBytes("hello");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        var fetcher = new FakeFetcher(data);
        var temp = Directory.CreateTempSubdirectory();
        var client = new UpdaterClient(fetcher, temp.FullName);
        var path = await client.DownloadAndStageAsync(new UpdatePackageInfo(new Uri("http://test"), hash, "stable"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task DownloadAndStage_Throws_On_Hash_Mismatch()
    {
        var data = Encoding.UTF8.GetBytes("hello");
        var wrongHash = "0000";
        var fetcher = new FakeFetcher(data);
        var temp = Directory.CreateTempSubdirectory();
        var client = new UpdaterClient(fetcher, temp.FullName);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DownloadAndStageAsync(new UpdatePackageInfo(new Uri("http://test"), wrongHash, "stable")));
    }
}
