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

/// <summary>
/// MSAL-backed token provider with basic retry on transient errors.
/// </summary>
public sealed class MsalTokenProvider : ITokenProvider
{
    private readonly Func<TokenRequest, Microsoft.Identity.Client.IConfidentialClientApplication> _appFactory;
    private readonly int _maxAttempts;

    public MsalTokenProvider(Func<TokenRequest, Microsoft.Identity.Client.IConfidentialClientApplication> appFactory, int maxAttempts = 3)
    {
        _appFactory = appFactory;
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    public async Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? last = null;

        while (attempt < _maxAttempts)
        {
            attempt++;
            try
            {
                var app = _appFactory(request);
                var scopes = new[] { request.Scope };
                var result = await app.AcquireTokenForClient(scopes)
                    .WithSendX5C(true)
                    .ExecuteAsync(cancellationToken);

                return result.AccessToken;
            }
            catch (Microsoft.Identity.Client.MsalServiceException ex) when (IsTransient(ex))
            {
                last = ex;
                await BackoffAsync(attempt, cancellationToken);
            }
            catch (Microsoft.Identity.Client.MsalUiRequiredException ex)
            {
                last = ex;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                await BackoffAsync(attempt, cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException("Token acquisition failed.");
    }

    private static bool IsTransient(Microsoft.Identity.Client.MsalServiceException ex)
    {
        return ex.StatusCode == 429 || (int)ex.StatusCode >= 500;
    }

    private static Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = Math.Min(1000 * (int)Math.Pow(2, attempt - 1), 8000);
        return Task.Delay(delayMs, cancellationToken);
    }
}
