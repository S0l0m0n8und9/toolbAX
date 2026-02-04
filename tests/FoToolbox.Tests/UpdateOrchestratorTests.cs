using FoToolbox.Updater;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class UpdateOrchestratorTests
{
    private sealed class FakeFetcher : IUpdateFetcher
    {
        private readonly string _payload;
        private readonly byte[] _package;
        public FakeFetcher(string payload, byte[] package)
        {
            _payload = payload;
            _package = package;
        }

        public Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsoluteUri.Contains("manifest"))
            {
                Stream s = new MemoryStream(Encoding.UTF8.GetBytes(_payload));
                return Task.FromResult(s);
            }

            return Task.FromResult<Stream>(new MemoryStream(_package));
        }
    }

    [Fact]
    public async Task Orchestrator_Stages_Latest_From_Channel()
    {
        var packageBytes = Encoding.UTF8.GetBytes("pkg");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes));
        var manifest = $$"""
        [
          {"channel":"stable","uri":"http://package","hash":"{{hash}}"}
        ]
        """; 
        var fetcher = new FakeFetcher(manifest, packageBytes);
        var loader = new UpdateManifestLoader(fetcher);
        var temp = Directory.CreateTempSubdirectory();
        var updater = new UpdaterClient(fetcher, temp.FullName);
        var orch = new UpdateOrchestrator(loader, updater, new UpdateChannelConfig("stable", new Uri("http://manifest")));

        var staged = await orch.CheckAndStageAsync();
        Assert.NotNull(staged);
        Assert.True(File.Exists(staged!.StagedPath));
    }
}
