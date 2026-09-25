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
    private readonly object _stateGate = new();
    private readonly List<DualWriteToken> _pendingTokens = [];
    private readonly List<PendingGateway> _pendingGateways = [];
    private DualWriteToken? _token;
    private string? _gatewayBaseUrl;
    private string _incompleteReason = "Waiting for a trusted token exchange and matching gateway response.";

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

    public DualWriteToken? Token { get { lock (_stateGate) return _token; } }
    public string? GatewayBaseUrl { get { lock (_stateGate) return _gatewayBaseUrl; } }
    public bool IsComplete { get { lock (_stateGate) return IsCompleteUnderLock(); } }
    public string IncompleteReason { get { lock (_stateGate) return _incompleteReason; } }

    internal int PendingTokenCount { get { lock (_stateGate) return _pendingTokens.Count; } }
    internal int PendingGatewayCount { get { lock (_stateGate) return _pendingGateways.Count; } }

    /// <summary>
    /// Legacy adapter compatibility only. A response body without its committed request/status provenance
    /// is untrusted and is never parsed or retained.
    /// </summary>
    public bool ObserveTokenResponseBody(string? json)
    {
        lock (_stateGate)
        {
            if (!IsCompleteUnderLock())
                _incompleteReason = "The sign-in adapter must provide committed token-exchange provenance.";
        }
        return false;
    }

    /// <summary>
    /// Legacy URL-only compatibility. A URL cannot prove the bearer sent or the response status and never
    /// pins a gateway or creates a close-time fallback.
    /// </summary>
    public bool ObserveUrl(string? url)
    {
        lock (_stateGate)
        {
            if (!IsCompleteUnderLock())
                _incompleteReason = "Waiting for a successful gateway response carrying the captured bearer.";
        }
        return false;
    }

    public async Task<bool> ObserveTokenExchangeAsync(
        DualWriteTokenExchangeObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCompleteUnderLock()) return false;
        }

        var (token, reason) = await DualWriteTokenResponseValidator.ValidateCaptureAsync(
            observation,
            _tenantConstraint,
            _tenantResolver,
            _clock(),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCompleteUnderLock()) return false;
            if (token is null)
            {
                _incompleteReason = reason;
                return false;
            }

            var matchingGateway = _pendingGateways.FirstOrDefault(candidate =>
                string.Equals(candidate.AccessToken, token.AccessToken, StringComparison.Ordinal));
            if (matchingGateway is not null)
            {
                CompleteUnderLock(token, matchingGateway.Origin);
                return true;
            }

            _pendingTokens.RemoveAll(candidate =>
                string.Equals(candidate.AccessToken, token.AccessToken, StringComparison.Ordinal));
            AddBounded(_pendingTokens, token);
            _incompleteReason = "Trusted token captured; waiting for a matching successful gateway response.";
            return true;
        }
    }

    public bool ObserveGatewayResponse(DualWriteGatewayResponseObservation observation)
    {
        if (observation is null ||
            observation.ResponseStatusCode is < 200 or > 299 ||
            !DualWriteEndpointPolicy.IsGatewayApiRequest(observation.RequestUri) ||
            !DualWriteEndpointPolicy.TryGetGatewayOrigin(observation.RequestUri, out var origin) ||
            !TryReadBearer(observation.Authorization, out var accessToken))
        {
            lock (_stateGate)
            {
                if (!IsCompleteUnderLock())
                    _incompleteReason = "Gateway response was not accepted for sign-in correlation.";
            }
            return false;
        }

        lock (_stateGate)
        {
            if (IsCompleteUnderLock()) return false;
            var matchingToken = _pendingTokens.FirstOrDefault(candidate =>
                string.Equals(candidate.AccessToken, accessToken, StringComparison.Ordinal));
            if (matchingToken is not null)
            {
                CompleteUnderLock(matchingToken, origin);
                return true;
            }

            _pendingGateways.RemoveAll(candidate =>
                string.Equals(candidate.AccessToken, accessToken, StringComparison.Ordinal));
            AddBounded(_pendingGateways, new PendingGateway(accessToken, origin));
            _incompleteReason = "Trusted gateway response observed; waiting for its provenanced token exchange.";
            return false;
        }
    }

    public static bool IsTokenEndpoint(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        DualWriteEndpointPolicy.IsTokenEndpoint(uri);

    public DualWriteSignInResult? Result
    {
        get
        {
            lock (_stateGate)
                return IsCompleteUnderLock() ? new DualWriteSignInResult(_token!, _gatewayBaseUrl!) : null;
        }
    }

    /// <summary>No manual-close fallback exists until full token/gateway correlation is complete.</summary>
    public DualWriteSignInResult? BestEffortResult => Result;

    private bool IsCompleteUnderLock() => _token is not null && _gatewayBaseUrl is not null;

    private void CompleteUnderLock(DualWriteToken token, Uri gatewayOrigin)
    {
        _token = token;
        _gatewayBaseUrl = gatewayOrigin.AbsoluteUri;
        _pendingTokens.Clear();
        _pendingGateways.Clear();
        _incompleteReason = string.Empty;
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
        return token.Length > 0 && !token.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
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
