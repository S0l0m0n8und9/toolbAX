using FoToolbox.Core.Models;
using Microsoft.Identity.Client;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// MSAL-based token provider for client credentials.
/// </summary>
public sealed class MsalTokenProvider : ITokenProvider
{
    private readonly string _authorityBase;
    private readonly Func<ServicePrincipal, CancellationToken, Task<ClientCredential>> _credentialProvider;
    private readonly int _maxAttempts;
    private readonly ConcurrentDictionary<string, IConfidentialClientApplication> _apps = new();

    [Obsolete("Use the async credential-provider constructor overload.")]
    public MsalTokenProvider(string authorityBase, Func<ServicePrincipal, ClientCredential> credentialProvider, int maxAttempts = 3)
        : this(authorityBase, (sp, _) => Task.FromResult(credentialProvider(sp)), maxAttempts)
    {
    }

    public MsalTokenProvider(string authorityBase, Func<ServicePrincipal, CancellationToken, Task<ClientCredential>> credentialProvider, int maxAttempts = 3)
    {
        _authorityBase = authorityBase.TrimEnd('/');
        _credentialProvider = credentialProvider;
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    /// <summary>
    /// Returns the cached <see cref="IConfidentialClientApplication"/> for the given
    /// (clientId, authority, credential fingerprint) triple, building a new one on first use
    /// or after credential rotation.
    /// </summary>
    internal IConfidentialClientApplication GetOrCreateApp(ServicePrincipal principal, string authority, ClientCredential credential)
    {
        var fingerprint = credential switch
        {
            ClientSecretCredential s => Sha256Hex(s.Secret),
            ClientCertificateCredential c => c.Certificate.Thumbprint,
            _ => throw new InvalidOperationException("Unsupported credential type.")
        };
        var cacheKey = $"{principal.ClientId}|{authority}|{fingerprint}";

        return _apps.GetOrAdd(cacheKey, _ =>
        {
            var appBuilder = ConfidentialClientApplicationBuilder
                .Create(principal.ClientId)
                .WithAuthority(authority);

            appBuilder = credential switch
            {
                ClientSecretCredential secret => appBuilder.WithClientSecret(secret.Secret),
                ClientCertificateCredential cert => appBuilder.WithCertificate(cert.Certificate),
                _ => throw new InvalidOperationException("Unsupported credential type.")
            };

            return appBuilder.Build();
        });
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    public async Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        Exception? last = null;

        var credential = await _credentialProvider(request.Principal, cancellationToken);
        var authority = $"{_authorityBase}/{request.TenantId}";
        var app = GetOrCreateApp(request.Principal, authority, credential);

        while (attempts < _maxAttempts)
        {
            attempts++;
            try
            {
                var result = await app.AcquireTokenForClient(new[] { request.Scope })
                    .WithSendX5C(true)
                    .WithForceRefresh(request.ForceRefresh)
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

public sealed record ClientSecretCredential(string Secret) : ClientCredential
{
    /// <summary>Synthesized record printing would leak the raw secret.</summary>
    public override string ToString() => "ClientSecretCredential(redacted)";
}

public sealed record ClientCertificateCredential(System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate) : ClientCredential
{
    /// <summary>Print only the thumbprint, never the certificate contents.</summary>
    public override string ToString() => $"ClientCertificateCredential({Certificate.Thumbprint})";
}
