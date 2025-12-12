using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

/// <summary>
/// Loads update manifest JSON and filters by channel.
/// </summary>
public sealed class UpdateManifestLoader
{
    private readonly IUpdateFetcher _fetcher;

    public UpdateManifestLoader(IUpdateFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    public async Task<UpdatePackageInfo?> LoadLatestAsync(UpdateChannelConfig channel, CancellationToken cancellationToken = default)
    {
        await using var stream = await _fetcher.FetchAsync(channel.ManifestUri, cancellationToken);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        var candidates = new List<UpdatePackageInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var ch = element.GetProperty("channel").GetString() ?? string.Empty;
            if (!string.Equals(ch, channel.Channel, StringComparison.OrdinalIgnoreCase)) continue;
            var uri = new Uri(element.GetProperty("uri").GetString() ?? throw new InvalidDataException("uri missing"));
            var hash = element.GetProperty("hash").GetString() ?? throw new InvalidDataException("hash missing");
            candidates.Add(new UpdatePackageInfo(uri, hash, ch));
        }

        return candidates.LastOrDefault();
    }
}
