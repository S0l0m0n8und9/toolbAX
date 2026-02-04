using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

/// <summary>
/// Coordinates manifest loading and package staging.
/// </summary>
public sealed class UpdateOrchestrator
{
    private readonly UpdateManifestLoader _manifestLoader;
    private readonly UpdaterClient _updaterClient;
    private readonly UpdateChannelConfig _channel;

    public UpdateOrchestrator(UpdateManifestLoader manifestLoader, UpdaterClient updaterClient, UpdateChannelConfig channel)
    {
        _manifestLoader = manifestLoader;
        _updaterClient = updaterClient;
        _channel = channel;
    }

    public async Task<UpdateStageResult?> CheckAndStageAsync(CancellationToken cancellationToken = default)
    {
        var info = await _manifestLoader.LoadLatestAsync(_channel, cancellationToken);
        if (info is null) return null;

        return await _updaterClient.DownloadAndStageAsync(info, cancellationToken);
    }
}
