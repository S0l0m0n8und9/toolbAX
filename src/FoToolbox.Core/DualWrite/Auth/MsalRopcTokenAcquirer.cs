using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Real ROPC acquirer using MSAL <c>AcquireTokenByUsernamePassword</c>.</summary>
public sealed class MsalRopcTokenAcquirer : IDataIntegratorTokenAcquirer
{
    public async Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct)
    {
        var app = PublicClientApplicationBuilder.Create(clientId).WithAuthority(authority).Build();
        try
        {
            var result = await app.AcquireTokenByUsernamePassword(new[] { scope }, username, password).ExecuteAsync(ct).ConfigureAwait(false);
            return new DualWriteToken(result.AccessToken, null, result.ExpiresOn);
        }
        catch (MsalException ex)
        {
            throw new DualWriteAuthException($"Data Integrator ROPC sign-in failed: {ex.ErrorCode}. {ex.Message}");
        }
    }
}
