using System;

namespace FoToolbox.Updater;

public sealed record UpdatePackageInfo(Uri PackageUri, string Hash, string Channel);

public sealed record UpdateChannelConfig(string Channel, Uri ManifestUri);
