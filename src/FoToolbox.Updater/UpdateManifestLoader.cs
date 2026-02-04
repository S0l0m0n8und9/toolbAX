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
            var version = element.TryGetProperty("version", out var verEl) ? verEl.GetString() : null;
            var rollbackUri = element.TryGetProperty("rollbackUri", out var rbUriEl) && rbUriEl.ValueKind == JsonValueKind.String
                ? new Uri(rbUriEl.GetString() ?? throw new InvalidDataException("rollbackUri missing"))
                : null;
            var rollbackHash = element.TryGetProperty("rollbackHash", out var rbHashEl) ? rbHashEl.GetString() : null;
            candidates.Add(new UpdatePackageInfo(uri, hash, ch, version, rollbackUri, rollbackHash));
        }

        if (candidates.Count == 0) return null;

        var versioned = candidates
            .Select(c => (Package: c, Parsed: TryParseVersion(c.Version)))
            .Where(c => c.Parsed is not null)
            .ToList();

        if (versioned.Count > 0)
        {
            return versioned
                .OrderBy(c => c.Parsed)
                .Select(c => c.Package)
                .Last();
        }

        return candidates.LastOrDefault();
    }

    private static Version? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        return Version.TryParse(version, out var parsed) ? parsed : null;
    }
}
