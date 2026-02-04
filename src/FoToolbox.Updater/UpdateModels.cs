using System;

namespace FoToolbox.Updater;

public sealed record UpdatePackageInfo(
    Uri PackageUri,
    string Hash,
    string Channel,
    string? Version = null,
    Uri? RollbackUri = null,
    string? RollbackHash = null);

public sealed record UpdateStageResult(string StagedPath, string? RollbackPath);

public sealed record UpdateChannelConfig(string Channel, Uri ManifestUri);
