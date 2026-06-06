using System;

namespace ToolBax.Core.Models;

/// <summary>Connection state of an environment (drives the status-dot colour).</summary>
public enum EnvStatus
{
    Connected,
    TokenExpired,
    Disconnected,
}

/// <summary>
/// How the (always delegated, never app-only) Data Integrator token is acquired. ROPC uses a stored
/// service-account credential and fails under MFA (AADSTS50076); Interactive uses a browser sign-in.
/// </summary>
public enum DiAuthMode
{
    Interactive,
    Ropc,
}

/// <summary>
/// An F&amp;O environment profile. Shared by the shell's environment switcher and the Profiles screen.
/// (Auth/Dataverse/Data-Integrator detail lands with the auth tabs; persistence is via
/// <see cref="ToolBax.Core.Services.IProfileStore"/>.)
/// </summary>
public sealed record EnvProfile(
    string Id,
    string Name,
    string Url,
    string Tenant,
    string Legal,
    string Tier,
    EnvStatus Status,
    int? LatencyMs = null,
    string? DataverseUrl = null,
    string? DataIntegratorClientId = null,
    DiAuthMode DataIntegratorMode = DiAuthMode.Interactive)
{

    /// <summary>List-item subtitle, e.g. "USMF · Tier 1".</summary>
    public string Subtitle => $"{Legal} · {Tier}";

    /// <summary>Compare-picker label, e.g. "USMF · USMF Dev".</summary>
    public string PickerLabel => $"{Legal} · {Name}";

    /// <summary>Two-letter chip initials derived from the name (e.g. "USMF Dev" → "UD").</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
            };
        }
    }
}
