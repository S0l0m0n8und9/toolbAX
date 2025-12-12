using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

/// <summary>
/// Responsible for downloading, validating, and staging update packages.
/// </summary>
public sealed class UpdaterClient
{
    private readonly IUpdateFetcher _fetcher;
    private readonly string _stagingRoot;

    public UpdaterClient(IUpdateFetcher fetcher, string stagingRoot)
    {
        _fetcher = fetcher;
        _stagingRoot = stagingRoot;
    }

    public async Task<string> DownloadAndStageAsync(UpdatePackageInfo package, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_stagingRoot);
        var stagingPath = Path.Combine(_stagingRoot, $"update-{DateTime.UtcNow:yyyyMMddHHmmss}.bin");

        await using var source = await _fetcher.FetchAsync(package.PackageUri, cancellationToken);
        await using (var target = new FileStream(stagingPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
            target.Position = 0;
            ValidateHash(target, package.Hash);
        }

        return stagingPath;
    }

    private static void ValidateHash(Stream stream, string expectedHash)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var hex = Convert.ToHexString(hash);
        if (!string.Equals(hex, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Hash mismatch for downloaded package. Expected {expectedHash}, got {hex}.");
        }
    }
}
