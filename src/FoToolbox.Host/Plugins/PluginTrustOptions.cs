using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Host.Plugins;

public sealed record PluginTrustOptions(bool AllowUnsigned, IReadOnlyCollection<string> AllowedThumbprints)
{
    public static PluginTrustOptions Default => new(false, Array.Empty<string>());

    public static PluginTrustOptions FromEnvironment()
    {
        var allowUnsignedEnv = Environment.GetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS");
        var allowUnsigned = string.Equals(allowUnsignedEnv, "true", StringComparison.OrdinalIgnoreCase);

        var thumbsEnv = Environment.GetEnvironmentVariable("FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS");
        var thumbs = (thumbsEnv ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        return new PluginTrustOptions(allowUnsigned, thumbs);
    }
}
