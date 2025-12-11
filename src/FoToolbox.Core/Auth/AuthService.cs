using FoToolbox.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Acquires tokens for F&O using pluggable token providers (MSAL by default).
/// </summary>
public sealed class AuthService
{
    private readonly ITokenProvider _tokenProvider;

    public AuthService(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<string> AcquireTokenAsync(FoEnvironment env, ServicePrincipal sp, CancellationToken cancellationToken = default)
    {
        var scope = $"{env.BaseUrl.TrimEnd('/')}/.default";
        var request = new TokenRequest(scope, env.TenantId, sp);

        var attempts = 0;
        Exception? last = null;
        while (attempts < 3)
        {
            attempts++;
            try
            {
                return await _tokenProvider.GetTokenAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempts < 3)
            {
                last = ex;
                await Task.Delay(200 * attempts, cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                break;
            }
        }

        throw last ?? new InvalidOperationException("Token acquisition failed.");
    }
}

public interface ITokenProvider
{
    Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default);
}

public sealed record TokenRequest(string Scope, string TenantId, ServicePrincipal Principal);
