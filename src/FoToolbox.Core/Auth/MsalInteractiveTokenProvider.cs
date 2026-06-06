using Microsoft.Identity.Client;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// MSAL public-client interactive token provider. Acquires a delegated user token via the system
/// browser with a loopback redirect — the canonical desktop pattern, requiring no embedded
/// browser/WebView2. The app registration must permit a public-client/<c>http://localhost</c>
/// redirect for this to succeed.
///
/// The MSAL token cache is persisted (DPAPI) via an <see cref="IMsalTokenCacheStore"/>, so after a
/// first interactive sign-in the provider renews tokens <b>silently</b> (no browser prompt) until
/// the refresh token expires — across operations and app restarts.
/// </summary>
public sealed class MsalInteractiveTokenProvider : IInteractiveTokenProvider
{
    private readonly IMsalTokenCacheStore _cacheStore;

    public MsalInteractiveTokenProvider()
        : this(DefaultCacheStore())
    {
    }

    public MsalInteractiveTokenProvider(IMsalTokenCacheStore cacheStore)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
    }

    public static string BuildAuthority(string authorityBase, string tenantId) =>
        $"{authorityBase.TrimEnd('/')}/{tenantId}";

    public static string BuildScope(string resourceBaseUrl) =>
        $"{resourceBaseUrl.TrimEnd('/')}/.default";

    public async Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var app = BuildApp(request);
        var scope = BuildScope(request.ResourceBaseUrl);
        var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();

        AuthenticationResult result;
        try
        {
            // Silent-first: reuse the cached session/refresh token when one exists.
            result = account is not null
                ? await app.AcquireTokenSilent(new[] { scope }, account).ExecuteAsync(cancellationToken).ConfigureAwait(false)
                : await AcquireInteractiveAsync(app, scope, cancellationToken).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            // Cached token can't be renewed silently (expired/revoked/needs consent): fall back to interactive.
            result = await AcquireInteractiveAsync(app, scope, cancellationToken).ConfigureAwait(false);
        }

        return new InteractiveTokenResult(result.AccessToken, result.ExpiresOn);
    }

    private static Task<AuthenticationResult> AcquireInteractiveAsync(IPublicClientApplication app, string scope, CancellationToken cancellationToken) =>
        app.AcquireTokenInteractive(new[] { scope })
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);

    private IPublicClientApplication BuildApp(InteractiveTokenRequest request)
    {
        var app = PublicClientApplicationBuilder
            .Create(request.ClientId)
            .WithAuthority(BuildAuthority(request.AuthorityBase, request.TenantId))
            .WithRedirectUri(request.RedirectUri)
            .Build();

        var cacheKey = $"{request.ClientId}|{request.TenantId}";
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            // Cache read is best-effort: a missing/corrupt cache must not break sign-in — just
            // start from an empty cache and acquire interactively.
            try
            {
                var blob = _cacheStore.Load(cacheKey);
                if (blob is not null)
                {
                    args.TokenCache.DeserializeMsalV3(blob);
                }
            }
            catch (Exception)
            {
                // Ignore: treat as no cached session.
            }
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            // Cache write is best-effort: the token was already acquired successfully. A persist
            // failure (disk full, permissions, DPAPI) must not fail the sign-in — the user is just
            // re-prompted next time because the entry wasn't saved.
            try
            {
                _cacheStore.Save(cacheKey, args.TokenCache.SerializeMsalV3());
            }
            catch (Exception)
            {
                // Ignore: persistence is an optimisation, not required for this acquisition.
            }
        });

        return app;
    }

    private static void Validate(InteractiveTokenRequest request)
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
    }

    private static IMsalTokenCacheStore DefaultCacheStore() =>
        // DPAPI cache is Windows-only; elsewhere fall back to a transient in-memory cache (the host
        // can inject a platform-appropriate persistent store).
        OperatingSystem.IsWindows()
            ? new DpapiFileMsalTokenCacheStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FoToolbox",
                "msal-cache"))
            : new InMemoryMsalTokenCacheStore();
}
