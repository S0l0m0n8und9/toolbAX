using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// ROPC acquirer using MSAL. The <see cref="IPublicClientApplication"/> is cached per
/// (authority, clientId) so MSAL's in-process token cache persists: after the first
/// username/password sign-in, subsequent acquisitions try a silent refresh first and only
/// re-send the password when MSAL has no usable cached token.
/// </summary>
public sealed class MsalRopcTokenAcquirer : IDataIntegratorTokenAcquirer
{
    private readonly ConcurrentDictionary<string, IPublicClientApplication> _apps = new();

    public async Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct)
    {
        var app = _apps.GetOrAdd(
            $"{authority}|{clientId}",
            _ => PublicClientApplicationBuilder.Create(clientId).WithAuthority(authority).Build());
        var scopes = new[] { scope };

        try
        {
            // Silent-first: reuse MSAL's cached token/refresh token when one exists.
            var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (account is not null)
            {
                try
                {
                    var silent = await app.AcquireTokenSilent(scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
                    return new DualWriteToken(silent.AccessToken, null, silent.ExpiresOn);
                }
                catch (MsalUiRequiredException)
                {
                    // Cached token can't be renewed silently — fall back to ROPC below.
                }
            }

            var result = await app.AcquireTokenByUsernamePassword(scopes, username, password).ExecuteAsync(ct).ConfigureAwait(false);
            return new DualWriteToken(result.AccessToken, null, result.ExpiresOn);
        }
        catch (MsalException ex)
        {
            throw new DualWriteAuthException($"Data Integrator ROPC sign-in failed: {ex.ErrorCode}. {ex.Message}");
        }
    }
}
