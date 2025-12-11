using FoToolbox.Core.Models;
using Microsoft.Identity.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// MSAL-based token provider for client credentials.
/// </summary>
public sealed class MsalTokenProvider : ITokenProvider
{
    private readonly string _authorityBase;
    private readonly Func<ServicePrincipal, ClientCredential> _credentialProvider;
    private readonly int _maxAttempts;

    public MsalTokenProvider(string authorityBase, Func<ServicePrincipal, ClientCredential> credentialProvider, int maxAttempts = 3)
    {
        _authorityBase = authorityBase.TrimEnd('/');
        _credentialProvider = credentialProvider;
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    public async Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        Exception? last = null;

        while (attempts < _maxAttempts)
        {
            attempts++;
            try
            {
                var credential = _credentialProvider(request.Principal);
                var authority = $"{_authorityBase}/{request.TenantId}";
                var appBuilder = ConfidentialClientApplicationBuilder
                    .Create(request.Principal.ClientId)
                    .WithAuthority(authority);

                if (credential is ClientSecretCredential secret)
                {
                    appBuilder = appBuilder.WithClientSecret(secret.Secret);
                }
                else if (credential is ClientCertificateCredential cert)
                {
                    appBuilder = appBuilder.WithCertificate(cert.Certificate);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported credential type.");
                }

                var app = appBuilder.Build();
                var result = await app.AcquireTokenForClient(new[] { request.Scope })
                    .WithSendX5C(true)
                    .ExecuteAsync(cancellationToken);

                return result.AccessToken;
            }
            catch (MsalServiceException ex) when (ex.StatusCode == 429 || (int)ex.StatusCode >= 500)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempts), cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempts >= _maxAttempts) break;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempts), cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException("Token acquisition failed.");
    }
}

public abstract record ClientCredential;
public sealed record ClientSecretCredential(string Secret) : ClientCredential;
public sealed record ClientCertificateCredential(System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate) : ClientCredential;
