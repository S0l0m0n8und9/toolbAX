using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Identifier of the account the last successful acquisition on this instance used, so the next
    /// silent renewal prefers the same identity instead of whatever the cache happens to enumerate
    /// first. In-memory and per-instance by design — see <see cref="SelectAccount"/> for the ceiling.
    /// </summary>
    private string? _lastUsedAccountId;

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
        var account = SelectAccount(
            await app.GetAccountsAsync().ConfigureAwait(false), _lastUsedAccountId, request.TenantId);

        AuthenticationResult result;
        try
        {
            // Silent-first: reuse the cached session/refresh token when one exists. ForceRefresh
            // bypasses the cached access token (refreshing from the STS) so "Test connection" can
            // prove the token is live, not just present in the cache.
            result = account is not null
                ? await app.AcquireTokenSilent(new[] { scope }, account)
                    .WithForceRefresh(request.ForceRefresh)
                    .ExecuteAsync(cancellationToken).ConfigureAwait(false)
                : await AcquireInteractiveAsync(app, scope, account, cancellationToken).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            // Cached token can't be renewed silently (expired/revoked/needs consent): fall back to
            // interactive, pinned to the SAME account the silent path just attempted. Without the pin
            // MSAL shows the generic picker, the user can complete sign-in as a different identity, and
            // the next silent attempt selects the original account again — re-prompting forever.
            result = await AcquireInteractiveAsync(app, scope, account, cancellationToken).ConfigureAwait(false);
        }

        // Remember what actually worked, so subsequent acquisitions on this instance stay bound to that
        // identity rather than re-picking from the cache's enumeration order.
        _lastUsedAccountId = result.Account?.HomeAccountId?.Identifier ?? _lastUsedAccountId;

        return new InteractiveTokenResult(result.AccessToken, result.ExpiresOn);
    }

    /// <summary>
    /// Chooses which cached account the silent path renews against, in priority order:
    /// (1) the account the last successful acquisition on this provider used, (2) an account whose home
    /// tenant matches the authority's tenant, (3) the first account in the cache.
    /// <para>
    /// The old behaviour was (3) alone, which in a two-account cache could bind the wrong or a stale
    /// account, fail silent renewal, and then re-prompt against an unpinned picker indefinitely.
    /// </para>
    /// <para>
    /// <b>Ceiling:</b> the preference is in-memory and per-provider-instance, so it resets on restart and
    /// is shared by every profile that routes through the same provider (all of them, today — the hosts
    /// share one instance to share the token cache). If wrong-account binding recurs, the real fix is
    /// persisting the chosen account identifier <i>per profile</i> and passing it in on the request.
    /// </para>
    /// </summary>
    internal static IAccount? SelectAccount(IEnumerable<IAccount>? accounts, string? lastUsedAccountId, string? tenantId)
    {
        var candidates = accounts?.Where(a => a is not null).ToList();
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(lastUsedAccountId))
        {
            var remembered = candidates.FirstOrDefault(a =>
                string.Equals(a.HomeAccountId?.Identifier, lastUsedAccountId, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            var sameTenant = candidates.FirstOrDefault(a =>
                string.Equals(a.HomeAccountId?.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            if (sameTenant is not null)
            {
                return sameTenant;
            }
        }

        return candidates[0];
    }

    private static Task<AuthenticationResult> AcquireInteractiveAsync(
        IPublicClientApplication app, string scope, IAccount? account, CancellationToken cancellationToken)
    {
        var builder = app.AcquireTokenInteractive(new[] { scope }).WithUseEmbeddedWebView(false);

        // Pin the prompt to the identity being renewed when we know it; with no cached account there is
        // nothing to hint and the picker is the correct experience (first sign-in).
        return (account is not null ? builder.WithAccount(account) : builder).ExecuteAsync(cancellationToken);
    }

    public async Task SignOutAsync(string clientId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        // Sign-out only needs the cache-key inputs — no resource/scope is involved.
        var app = BuildApp(clientId, tenantId);

        // Remove every cached account (MSAL clears its in-cache entries; the AfterAccess hook persists
        // the now-emptied cache), then delete the persisted blob outright so nothing survives a restart.
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        foreach (var account in accounts)
        {
            await app.RemoveAsync(account).ConfigureAwait(false);
        }

        _cacheStore.Remove($"{clientId}|{tenantId}");
    }

    private IPublicClientApplication BuildApp(InteractiveTokenRequest request) =>
        BuildApp(request.ClientId, request.TenantId, request.AuthorityBase, request.RedirectUri);

    // The resource/scope is not needed to build the app or key its cache — only these four inputs are.
    // Keeping this overload explicit lets sign-out evict by (clientId, tenantId) without inventing a
    // placeholder resource URL, so a future change to how the app is built can't silently break sign-out.
    private IPublicClientApplication BuildApp(
        string clientId,
        string tenantId,
        string authorityBase = "https://login.microsoftonline.com",
        string redirectUri = "http://localhost")
    {
        var app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(BuildAuthority(authorityBase, tenantId))
            .WithRedirectUri(redirectUri)
            .Build();

        var cacheKey = $"{clientId}|{tenantId}";
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
