namespace FoToolbox.Host.Plugins;

/// <summary>The user's trust decision for an unsigned third-party plugin.</summary>
public enum PluginConsentDecision
{
    /// <summary>Do not load the plugin.</summary>
    Deny = 0,

    /// <summary>Load for this session only; do not persist.</summary>
    LoadOnce = 1,

    /// <summary>Load and remember (persist to the trust store).</summary>
    AlwaysTrust = 2
}

/// <summary>Details shown to the user when asking whether to load an unsigned plugin.</summary>
public sealed record PluginConsentRequest(string AssemblyName, string AssemblyPath, string Sha256);

/// <summary>
/// Abstraction over the user consent prompt for unsigned third-party plugins.
/// Implemented by the host UI; left null in headless/test contexts (which then deny).
/// </summary>
public interface IPluginConsentPrompt
{
    PluginConsentDecision RequestConsent(PluginConsentRequest request);
}
