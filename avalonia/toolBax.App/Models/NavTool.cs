namespace ToolBax.App.Models;

/// <summary>
/// A tool the shell can navigate to (nav rail item + command-palette entry). <see cref="Shortcut"/>
/// is the Alt+&lt;letter&gt; accelerator ('\0' = none, e.g. the Plugins home).
/// </summary>
public sealed record NavTool(string Id, string Title, char Shortcut, bool IsLive = false)
{
    /// <summary>Display form of the accelerator, e.g. "Alt+O" (empty when there is none).</summary>
    public string ShortcutLabel => Shortcut == '\0' ? string.Empty : $"Alt+{Shortcut}";
}
