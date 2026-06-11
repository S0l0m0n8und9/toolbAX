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

/// <summary>Friendly labels for the Data Integrator auth modes (Profiles DI tab dropdown).</summary>
public static class DiAuthModeExtensions
{
    /// <summary>
    /// The well-known first-party Data Integrator client id. The dual-write sign-in uses this fixed
    /// Microsoft app (the WPF/original tool never asks the user for a client id); kept in sync with
    /// <c>FoToolbox.Core.DualWrite.Auth.DualWriteAuthConstants.ClientId</c> (a drift guard test asserts
    /// the two match). It's the default for the Profiles DI Client ID field, but stays editable.
    /// </summary>
    public const string DefaultDataIntegratorClientId = "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b";

    public static string Label(this DiAuthMode mode) => mode switch
    {
        DiAuthMode.Interactive => "Interactive (MFA)",
        DiAuthMode.Ropc => "ROPC (service account)",
        _ => mode.ToString(),
    };
}

/// <summary>
/// How an F&amp;O / Dataverse connection authenticates. <see cref="Interactive"/> is a delegated browser
/// sign-in (MFA-capable, no stored secret — the default); ClientSecret/Certificate are app-only
/// (client-credentials) service-principal modes that mirror the FoToolbox.Core AuthMode values.
/// </summary>
public enum FoAuthMode
{
    Interactive,
    ClientSecret,
    Certificate,
}

/// <summary>Friendly labels for the F&amp;O / Dataverse auth modes (Profiles dropdowns).</summary>
public static class FoAuthModeExtensions
{
    /// <summary>The default global public client ID Microsoft provides for interactive sign-in.</summary>
    public const string DefaultInteractiveClientId = "2ad88395-b77d-4561-9441-d0e40824f9bc";

    public static string Label(this FoAuthMode mode) => mode switch
    {
        FoAuthMode.Interactive => "Interactive (MFA)",
        FoAuthMode.ClientSecret => "Client secret",
        FoAuthMode.Certificate => "Certificate",
        _ => mode.ToString(),
    };
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
    DiAuthMode DataIntegratorMode = DiAuthMode.Interactive,
    string? ClientId = null,
    FoAuthMode AuthMode = FoAuthMode.Interactive,
    string? DataverseClientId = null,
    FoAuthMode DataverseAuthMode = FoAuthMode.Interactive,
    string? DualWriteGatewayUrl = null)
{
    /// <summary>The two environment-type buckets offered by the Profiles "Environment type" dropdown
    /// (the free-text <see cref="Tier"/> is normalised to one of these for display + editing).</summary>
    public const string ProductionType = "Production";

    public const string NonProductionType = "Non-production";

    /// <summary>
    /// Normalises a free-text tier (e.g. "Prod", "Tier 1", "Sandbox") to one of the two environment-type
    /// buckets: anything starting with "prod" (case-insensitive) is Production, everything else
    /// Non-production. Mirrors the design prototype's normalisation so legacy tier strings map cleanly.
    /// </summary>
    public static string NormalizeEnvironmentType(string? tier) =>
        !string.IsNullOrWhiteSpace(tier) &&
        tier.TrimStart().StartsWith("prod", StringComparison.OrdinalIgnoreCase)
            ? ProductionType
            : NonProductionType;

    /// <summary>Production / Non-production bucket derived from <see cref="Tier"/>.</summary>
    public string EnvironmentType => NormalizeEnvironmentType(Tier);

    /// <summary>List-item subtitle, e.g. "USMF · Non-production".</summary>
    public string Subtitle => $"{Legal} · {EnvironmentType}";

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
