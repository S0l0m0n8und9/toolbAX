using System;
using System.Windows.Media;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.Host.Plugins;

internal static class IconResourceResolver
{
    private const string DefaultKey = "Icon.Plugin";

    public static Geometry? Resolve(FoPluginManifest manifest, Func<string, Geometry?> lookup)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));

        if (!string.IsNullOrWhiteSpace(manifest.Icon))
        {
            var explicitGeom = lookup("Icon." + manifest.Icon);
            if (explicitGeom is not null) return explicitGeom;
        }

        var heuristicKey = HeuristicKeyFor(manifest.Name);
        var heuristicGeom = lookup(heuristicKey);
        if (heuristicGeom is not null) return heuristicGeom;

        return lookup(DefaultKey);
    }

    public static Geometry? Resolve(string name, Func<string, Geometry?> lookup)
    {
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));
        return lookup(HeuristicKeyFor(name)) ?? lookup(DefaultKey);
    }

    private static string HeuristicKeyFor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return DefaultKey;

        if (Contains(name, "Profile")) return "Icon.Profiles";
        if (Contains(name, "Query")) return "Icon.Query";
        if (Contains(name, "Dual")) return "Icon.DualWrite";
        if (Contains(name, "POST")) return "Icon.ODataPost";
        if (Contains(name, "Table") || Contains(name, "Entity") || Contains(name, "Metadata"))
            return "Icon.TableEntity";

        return DefaultKey;
    }

    private static bool Contains(string s, string fragment) =>
        s.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
