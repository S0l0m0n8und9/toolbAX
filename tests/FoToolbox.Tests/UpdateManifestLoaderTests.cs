using FoToolbox.Updater;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class UpdateManifestLoaderTests
{
    private sealed class FakeFetcher : IUpdateFetcher
    {
        private readonly string _payload;
        public FakeFetcher(string payload) => _payload = payload;
        public Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Stream s = new MemoryStream(Encoding.UTF8.GetBytes(_payload));
            return Task.FromResult(s);
        }
    }

    [Fact]
    public async Task Picks_Latest_For_Channel()
    {
        var manifest = """
        [
          {"channel":"stable","uri":"http://a","hash":"H1","version":"0.1.0"},
          {"channel":"stable","uri":"http://b","hash":"H2","version":"0.2.0"}
        ]
        """;
        var loader = new UpdateManifestLoader(new FakeFetcher(manifest));
        var info = await loader.LoadLatestAsync(new UpdateChannelConfig("stable", new Uri("http://manifest")));
        Assert.NotNull(info);
        Assert.Contains("http://b", info!.PackageUri.ToString());
        Assert.Equal("H2", info.Hash);
        Assert.Equal("0.2.0", info.Version);
    }

    [Fact]
    public async Task Returns_Null_When_No_Channel()
    {
        var manifest = """[{"channel":"beta","uri":"http://a","hash":"H"}]""";
        var loader = new UpdateManifestLoader(new FakeFetcher(manifest));
        var info = await loader.LoadLatestAsync(new UpdateChannelConfig("stable", new Uri("http://manifest")));
        Assert.Null(info);
    }

    [Fact]
    public async Task Parses_Rollback_Fields_When_Present()
    {
        var manifest = """
        [
          {"channel":"stable","uri":"http://a","hash":"H1","rollbackUri":"http://rb","rollbackHash":"RH"}
        ]
        """;
        var loader = new UpdateManifestLoader(new FakeFetcher(manifest));
        var info = await loader.LoadLatestAsync(new UpdateChannelConfig("stable", new Uri("http://manifest")));
        Assert.NotNull(info);
        Assert.Equal(new Uri("http://rb"), info!.RollbackUri);
        Assert.Equal("RH", info.RollbackHash);
    }
}
