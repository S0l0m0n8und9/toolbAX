using System;

namespace FoToolbox.SDK;

/// <summary>
/// SDK version used for manifest gating.
/// </summary>
public static class SdkInfo
{
    public static Version Version { get; } = new Version(0, 2, 0, 0);
}
