using Microsoft.Identity.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// MSAL public-client interactive token provider. Acquires a delegated user token via the system
/// browser with a loopback redirect — the canonical desktop pattern, requiring no embedded
/// browser/WebView2. The app registration must permit a public-client/<c>http://localhost</c>
/// redirect for this to succeed.
/// </summary>
public sealed class MsalInteractiveTokenProvider : IInteractiveTokenProvider
{
    public static string BuildAuthority(string authorityBase, string tenantId) =>
        $"{authorityBase.TrimEnd('/')}/{tenantId}";

    public static string BuildScope(string resourceBaseUrl) =>
        $"{resourceBaseUrl.TrimEnd('/')}/.default";

    public async Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new ArgumentException("Client ID is required for interactive sign-in.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException("Tenant ID is required for interactive sign-in.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ResourceBaseUrl))
        {
            throw new ArgumentException("Resource base URL is required for interactive sign-in.", nameof(request));
        }

        var app = PublicClientApplicationBuilder
            .Create(request.ClientId)
            .WithAuthority(BuildAuthority(request.AuthorityBase, request.TenantId))
            .WithRedirectUri(request.RedirectUri)
            .Build();

        var result = await app
            .AcquireTokenInteractive(new[] { BuildScope(request.ResourceBaseUrl) })
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);

        return new InteractiveTokenResult(result.AccessToken, result.ExpiresOn);
    }
}
