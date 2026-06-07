using System;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Constants for the Dual-write (Data Integrator) delegated-auth flow, taken verbatim from
/// the MS tool (<c>DWLibary/TokenRefresh.cs</c>, <c>EdgeUniversal.cs</c>, <c>GlobalVar.cs</c>).
/// The interactive sign-in drives the Data Integrator portal in an embedded browser and
/// captures the delegated token + the regional gateway host from its network traffic;
/// renewal afterwards uses the clean refresh-token POST below (no browser).
/// </summary>
public static class DualWriteAuthConstants
{
    /// <summary>First-party Data Integrator / dual-write client id (well-known).</summary>
    public const string ClientId = "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b";

    /// <summary>The IntegratorApp resource base URL (the delegated token's audience).</summary>
    public const string ResourceBaseUrl = "https://IntegratorApp.com";

    /// <summary>Delegated scope for the IntegratorApp resource (+ offline_access for refresh tokens).</summary>
    public const string Scope = ResourceBaseUrl + "/.default openid profile offline_access";

    /// <summary>Entra v2 token endpoint (common authority), used for refresh.</summary>
    public const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    /// <summary>Data Integrator portal base; the interactive sign-in navigates here.</summary>
    public const string DataIntegratorBaseUrl = "https://dataintegrator.trafficmanager.net";

    /// <summary>Redirect URI registered for the portal (used as redirect_uri on the refresh POST).</summary>
    public const string RedirectUri = DataIntegratorBaseUrl + "/dualWrite";

    /// <summary>Substring that identifies a dual-write management gateway URL in browser traffic.</summary>
    public const string GatewayHostMarker = "projectmanagementservice";

    /// <summary>
    /// Substring that identifies a call to the actual DualWriteManagement API (e.g. the portal's
    /// <c>.../api/DualWriteManagement/1.0/Version</c> handshake). The MS tool keys on this to pick the
    /// environment's resolved regional gateway, not the first <see cref="GatewayHostMarker"/> host it
    /// sees (which can be a global/routing endpoint).
    /// </summary>
    public const string GatewayApiMarker = "DualWriteManagement";

    /// <summary>Builds the portal sign-in URL for the given F&amp;O environment identifier.</summary>
    public static string BuildSignInUrl(string foIdentifier) =>
        $"{DataIntegratorBaseUrl}/dualWrite?axenv={Uri.EscapeDataString(foIdentifier ?? string.Empty)}";
}
