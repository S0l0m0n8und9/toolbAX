using System;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>The captured outcome of an interactive sign-in: a delegated token + discovered gateway.</summary>
public sealed record DualWriteSignInResult(DualWriteToken Token, string GatewayBaseUrl);

/// <summary>
/// Pure state machine driven by an embedded browser during interactive sign-in. The browser
/// feeds it (a) the body of every Entra token-endpoint response and (b) the URL of every
/// request; this captures the delegated token from the token body and the regional gateway
/// host from the first <c>projectmanagementservice</c> URL it sees. UI-free so it is fully
/// unit-testable; the WebView2 window is a thin adapter over it.
/// </summary>
public sealed class DualWriteSignInCapture
{
    private readonly Func<DateTimeOffset> _clock;

    public DualWriteSignInCapture(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DualWriteToken? Token { get; private set; }

    /// <summary>
    /// The gateway host actually serving the DualWriteManagement API (the host of a URL carrying
    /// <see cref="DualWriteAuthConstants.GatewayApiMarker"/>). Null until such a call is observed.
    /// </summary>
    public string? GatewayBaseUrl { get; private set; }

    /// <summary>
    /// First bare <see cref="DualWriteAuthConstants.GatewayHostMarker"/> host seen, used only as a
    /// best-effort fallback if sign-in is closed before an API call pins the real regional gateway.
    /// </summary>
    private string? _fallbackGatewayBaseUrl;

    /// <summary>True once both the token and the API gateway host have been captured.</summary>
    public bool IsComplete => Token is not null && !string.IsNullOrWhiteSpace(GatewayBaseUrl);

    /// <summary>Feed a token-endpoint response body. Returns true if a token was parsed from it.</summary>
    public bool ObserveTokenResponseBody(string? json)
    {
        if (Token is not null)
        {
            return false;
        }

        var token = DualWriteTokenParser.Parse(json ?? string.Empty, _clock());
        if (token is null)
        {
            return false;
        }

        Token = token;
        return true;
    }

    /// <summary>
    /// Feed any request/response URL. Pins <see cref="GatewayBaseUrl"/> to the host of a
    /// DualWriteManagement API call (preferred, mirrors the MS tool's Version-call keying); a bare
    /// <see cref="DualWriteAuthConstants.GatewayHostMarker"/> host is only remembered as a fallback.
    /// Returns true when the API gateway host is (re)assigned.
    /// </summary>
    public bool ObserveUrl(string? url)
    {
        if (GatewayBaseUrl is not null || string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Anchor the match so the delegated token can only be pinned to an https host whose HOST LABEL
        // carries the gateway marker — not any URL that merely contains the marker in its path/query
        // (e.g. https://attacker.example/projectmanagementservice/DualWriteManagement). Require https so
        // the bearer is never pinned to a cleartext host.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.IndexOf(DualWriteAuthConstants.GatewayHostMarker, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var host = $"{uri.Scheme}://{uri.Host}";

        // Only the host serving the DualWriteManagement API (marker in the request PATH) is the
        // environment's real regional gateway. The first bare projectmanagementservice host may be a
        // global/routing endpoint that returns an empty environment list, so keep it only as a fallback.
        if (uri.AbsolutePath.IndexOf(DualWriteAuthConstants.GatewayApiMarker, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            GatewayBaseUrl = host;
            return true;
        }

        _fallbackGatewayBaseUrl ??= host;
        return false;
    }

    /// <summary>True if the URL is an Entra token endpoint (whose body should be observed).</summary>
    public static bool IsTokenEndpoint(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url!.IndexOf("/oauth2/v2.0/token", StringComparison.OrdinalIgnoreCase) >= 0 &&
        (url.IndexOf("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
         url.IndexOf("login.microsoft.com", StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>The captured result, or null if not yet complete.</summary>
    public DualWriteSignInResult? Result =>
        IsComplete ? new DualWriteSignInResult(Token!, GatewayBaseUrl!) : null;

    /// <summary>
    /// Best-effort result for when the user closes sign-in before an API call pinned the regional
    /// gateway: uses the API host if known, else the fallback projectmanagementservice host. Null if
    /// no token or no gateway host was seen at all.
    /// </summary>
    public DualWriteSignInResult? BestEffortResult
    {
        get
        {
            if (Token is null)
            {
                return null;
            }

            var gateway = GatewayBaseUrl ?? _fallbackGatewayBaseUrl;
            return string.IsNullOrWhiteSpace(gateway) ? null : new DualWriteSignInResult(Token, gateway);
        }
    }
}
