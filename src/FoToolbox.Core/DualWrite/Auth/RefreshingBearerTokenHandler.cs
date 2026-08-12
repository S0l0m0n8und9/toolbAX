using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Attaches the delegated bearer token to gateway requests and silently renews it via the
/// refresh token before expiry, invoking <c>onRefreshed</c> so the caller can persist the
/// rotated token. This is the browser-free renewal path that keeps a signed-in session alive.
/// Renewal has two triggers: our own clock says the token is at/near expiry, or the gateway
/// answers 401 for a token we still believed in (revoked, rotated server-side, clock skew).
/// </summary>
public sealed class RefreshingBearerTokenHandler : DelegatingHandler
{
    private readonly DualWriteRefreshTokenProvider _refresher;
    private readonly Func<DualWriteToken, Task>? _onRefreshed;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DualWriteToken _token;

    public RefreshingBearerTokenHandler(
        DualWriteToken token,
        DualWriteRefreshTokenProvider refresher,
        Func<DualWriteToken, Task>? onRefreshed = null,
        Func<DateTimeOffset>? clock = null)
        : base(new HttpClientHandler())
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _refresher = refresher ?? throw new ArgumentNullException(nameof(refresher));
        _onRefreshed = onRefreshed;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        var attempted = _token;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", attempted.AccessToken);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // The clock check above never sees a token the *gateway* has stopped accepting, so without this a
        // revoked/rotated token left every subsequent operation dying on a bare 401 until the user signed
        // in through the browser again. Exactly one refresh + replay: whatever the replay returns —
        // including another 401 — is the caller's answer, so a rejecting gateway can't spin us.
        if (response.StatusCode != HttpStatusCode.Unauthorized ||
            !await TryRefreshAfterUnauthorizedAsync(attempted, cancellationToken).ConfigureAwait(false))
        {
            return response;
        }

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
        // Safe to replay: every gateway body is a buffered StringContent, so it re-serializes.
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renews the token after a 401. Returns true when the request should be replayed. False means "no
    /// renewal was possible" (no refresh token, or the refresh itself was rejected) — the caller then
    /// surfaces the gateway's own 401 rather than an auth exception from a retry nobody asked for.
    /// </summary>
    private async Task<bool> TryRefreshAfterUnauthorizedAsync(DualWriteToken attempted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attempted.RefreshToken))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A concurrent request may already have rotated past the token this one used — replay with
            // theirs instead of burning a second refresh (and a second rotated refresh token).
            if (!ReferenceEquals(_token, attempted))
            {
                return true;
            }

            var refreshed = await _refresher.RefreshAsync(_token.RefreshToken!, cancellationToken).ConfigureAwait(false);
            _token = refreshed;
            if (_onRefreshed is not null)
            {
                await _onRefreshed(refreshed).ConfigureAwait(false);
            }

            return true;
        }
        catch (DualWriteAuthException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (!_token.IsExpired(_clock()) || string.IsNullOrWhiteSpace(_token.RefreshToken))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check under the lock — another request may have just refreshed.
            if (!_token.IsExpired(_clock()) || string.IsNullOrWhiteSpace(_token.RefreshToken))
            {
                return;
            }

            var refreshed = await _refresher.RefreshAsync(_token.RefreshToken!, cancellationToken).ConfigureAwait(false);
            _token = refreshed;
            if (_onRefreshed is not null)
            {
                await _onRefreshed(refreshed).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
