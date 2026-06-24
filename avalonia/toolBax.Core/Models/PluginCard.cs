using System;

namespace ToolBax.Core.Models;

/// <summary>
/// A built-in tool entry on the Plugins home card grid (control-map §1). These are native in-app
/// screens, not separately-versioned or separately-signed plugins, so the card carries only honest
/// metadata — no fabricated version or "signed" badge.
/// </summary>
public sealed record PluginCard(
    string Id,
    string Name,
    string Category,
    string Description,
    string Shortcut,
    bool OperatesLive = false)
{
    /// <summary>Footer accelerator, e.g. "Alt+Q".</summary>
    public string ShortcutLabel => $"Alt+{Shortcut}";

    /// <summary>Single-letter chip when no icon set is available.</summary>
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}
