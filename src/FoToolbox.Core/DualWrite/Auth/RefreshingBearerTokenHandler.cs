using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Attaches the delegated bearer token to gateway requests and silently renews it via the
/// refresh token before expiry, invoking <c>onRefreshed</c> so the caller can persist the
/// rotated token. This is the browser-free renewal path that keeps a signed-in session alive.
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
