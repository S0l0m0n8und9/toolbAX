using System;

namespace ToolBax.Core.Models;

/// <summary>A plugin/tool entry on the Plugins home card grid (control-map §1).</summary>
public sealed record PluginCard(
    string Id,
    string Name,
    string Category,
    string Version,
    string Description,
    string Shortcut,
    bool Signed,
    bool Live = false,
    bool Builtin = false,
    bool Hot = false)
{
    /// <summary>Mono caption under the name, e.g. "v1.4.2 · Data".</summary>
    public string VersionLine => $"v{Version} · {Category}";

    /// <summary>Footer accelerator, e.g. "Alt+Q".</summary>
    public string ShortcutLabel => $"Alt+{Shortcut}";

    /// <summary>Single-letter chip when no icon set is available.</summary>
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}
