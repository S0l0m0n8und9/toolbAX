using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>The captured outcome of an interactive sign-in: a bound delegated token + trusted gateway.</summary>
public sealed record DualWriteSignInResult(DualWriteToken Token, string GatewayBaseUrl);

/// <summary>
/// Correlates a provenance-checked committed token exchange with a successful trusted gateway response
/// that used the same opaque bearer. URL-only and body-only legacy observations never complete capture.
/// </summary>
public sealed class DualWriteSignInCapture
{
    private const int MaximumPending = 4;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _tenantConstraint;
    private readonly IDualWriteTenantResolver _tenantResolver;
    private readonly List<DualWriteToken> _pendingTokens = [];
    private readonly List<PendingGateway> _pendingGateways = [];

    public DualWriteSignInCapture(Func<DateTimeOffset>? clock = null)
        : this("common", null, clock)
    {
    }

    public DualWriteSignInCapture(
        string? tenantConstraint,
        IDualWriteTenantResolver? tenantResolver = null,
        Func<DateTimeOffset>? clock = null)
    {
        _tenantConstraint = tenantConstraint;
        _tenantResolver = tenantResolver ?? new DualWriteTenantResolver();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DualWriteToken? Token { get; private set; }
    public string? GatewayBaseUrl { get; private set; }
    public bool IsComplete => Token is not null && GatewayBaseUrl is not null;
    public string IncompleteReason { get; private set; } = "Waiting for a trusted token exchange and matching gateway response.";

    internal int PendingTokenCount => _pendingTokens.Count;
    internal int PendingGatewayCount => _pendingGateways.Count;

    /// <summary>
    /// Legacy adapter compatibility only. A response body without its committed request/status provenance
    /// is untrusted and is never parsed or retained.
    /// </summary>
    public bool ObserveTokenResponseBody(string? json)
    {
        IncompleteReason = "The sign-in adapter must provide committed token-exchange provenance.";
        return false;
    }

    /// <summary>
    /// Legacy URL-only compatibility. A URL cannot prove the bearer sent or the response status and never
    /// pins a gateway or creates a close-time fallback.
    /// </summary>
    public bool ObserveUrl(string? url)
    {
        IncompleteReason = "Waiting for a successful gateway response carrying the captured bearer.";
        return false;
    }

    public async Task<bool> ObserveTokenExchangeAsync(
        DualWriteTokenExchangeObservation observation,
        CancellationToken cancellationToken = default)
    {
        if (IsComplete)
        {
            return false;
        }

        var (token, reason) = await DualWriteTokenResponseValidator.ValidateCaptureAsync(
            observation,
            _tenantConstraint,
            _tenantResolver,
            _clock(),
            cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            IncompleteReason = reason;
            return false;
        }

        var matchingGateway = _pendingGateways.FirstOrDefault(candidate =>
            string.Equals(candidate.AccessToken, token.AccessToken, StringComparison.Ordinal));
        if (matchingGateway is not null)
        {
            Complete(token, matchingGateway.Origin);
            return true;
        }

        _pendingTokens.RemoveAll(candidate =>
            string.Equals(candidate.AccessToken, token.AccessToken, StringComparison.Ordinal));
        AddBounded(_pendingTokens, token);
        IncompleteReason = "Trusted token captured; waiting for a matching successful gateway response.";
        return true;
    }

    public bool ObserveGatewayResponse(DualWriteGatewayResponseObservation observation)
    {
        if (IsComplete || observation is null ||
            observation.ResponseStatusCode is < 200 or > 299 ||
            !DualWriteEndpointPolicy.IsGatewayApiRequest(observation.RequestUri) ||
            !DualWriteEndpointPolicy.TryGetGatewayOrigin(observation.RequestUri, out var origin) ||
            !TryReadBearer(observation.Authorization, out var accessToken))
        {
            IncompleteReason = "Gateway response was not accepted for sign-in correlation.";
            return false;
        }

        var matchingToken = _pendingTokens.FirstOrDefault(candidate =>
            string.Equals(candidate.AccessToken, accessToken, StringComparison.Ordinal));
        if (matchingToken is not null)
        {
            Complete(matchingToken, origin);
            return true;
        }

        _pendingGateways.RemoveAll(candidate =>
            string.Equals(candidate.AccessToken, accessToken, StringComparison.Ordinal));
        AddBounded(_pendingGateways, new PendingGateway(accessToken, origin));
        IncompleteReason = "Trusted gateway response observed; waiting for its provenanced token exchange.";
        return false;
    }

    public static bool IsTokenEndpoint(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        DualWriteEndpointPolicy.IsTokenEndpoint(uri);

    public DualWriteSignInResult? Result =>
        IsComplete ? new DualWriteSignInResult(Token!, GatewayBaseUrl!) : null;

    /// <summary>No manual-close fallback exists until full token/gateway correlation is complete.</summary>
    public DualWriteSignInResult? BestEffortResult => Result;

    private void Complete(DualWriteToken token, Uri gatewayOrigin)
    {
        Token = token;
        GatewayBaseUrl = gatewayOrigin.AbsoluteUri;
        _pendingTokens.Clear();
        _pendingGateways.Clear();
        IncompleteReason = string.Empty;
    }

    private static bool TryReadBearer(string? authorization, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        var separator = authorization.IndexOf(' ');
        if (separator < 1 ||
            !string.Equals(authorization[..separator], "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization[(separator + 1)..].Trim();
        return token.Length > 0 && !token.Any(char.IsWhiteSpace);
    }

    private static void AddBounded<T>(List<T> list, T value)
    {
        if (list.Count == MaximumPending)
        {
            list.RemoveAt(0);
        }
        list.Add(value);
    }

    private sealed record PendingGateway(string AccessToken, Uri Origin);
}
