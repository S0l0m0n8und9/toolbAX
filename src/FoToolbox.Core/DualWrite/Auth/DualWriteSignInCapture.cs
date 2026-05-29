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
    public string? GatewayBaseUrl { get; private set; }

    /// <summary>True once both the token and the gateway host have been captured.</summary>
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

    /// <summary>Feed any request/response URL. Returns true if it yielded the gateway host.</summary>
    public bool ObserveUrl(string? url)
    {
        if (GatewayBaseUrl is not null || string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.IndexOf(DualWriteAuthConstants.GatewayHostMarker, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        GatewayBaseUrl = $"{uri.Scheme}://{uri.Host}";
        return true;
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
}
