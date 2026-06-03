# Dual-write Operations — profile-level auth + default client id (revised)

Status: Draft for review (revised 2026-06-02). Supersedes the auth half of
`2026-05-29-dualwrite-operations-design.md`. Connection/feature behaviour from that spec is
unchanged. This revision replaces an earlier (invalidated) MSAL-loopback / `2ad88395` design.

## Goals (from the user)

1. The Dual-write Operations plugin should **not own its auth** — use a **profile-level** credential
   like the rest of toolbAX (so the Compare plugin can reuse it too).
2. Default the Data Integrator **client id** to `2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b`.
3. (Answered) "gateway base url" ≠ the wiki's `dataIntegratorURL` (the portal). See Background.

## Background — why the obvious approaches don't work (proven 2026-06-02)

The gateway requires a **delegated** token for the first-party Data Integrator resource
(`2e49aa60` = `https://IntegratorApp.com`). Empirically:
- **App-only / client-credentials**: not available — it's a delegated-only resource.
- **MSAL loopback-interactive** with a *registerable* client: blocked. `2ad88395` (and other
  Microsoft first-party clients) → `AADSTS65002` (the resource only issues tokens to clients
  **preauthorized by Microsoft**, which we cannot configure). The only client authorized for the
  resource is `2e49aa60` itself, and it has **no `http://localhost` redirect** → `AADSTS50011`. WAM
  broker did not produce a token either.
- So there are exactly **two** working token paths, matching the reference tool
  (`microsoft/Dual-write-automations`):
  - **Interactive** = drive the DI portal in a browser and **capture** the token (its `EdgeUniversal`
    uses Selenium/Edge CDP; toolbAX uses WebView2). Works with MFA. Renew via the refresh-token POST
    (`grant_type=refresh_token`, `client_id=2e49aa60`, `scope=https://IntegratorApp.com/.default
    openid profile offline_access`).
  - **ROPC** (the tool's misnamed `ServicePrincipalAuth`) = `UsernamePasswordCredential`
    (username + password + tenant + client id) requesting `https://IntegratorApp.com/.default`. **No
    browser, no redirect URI, no preauth issue** with `client_id=2e49aa60`. **MFA-incompatible**
    (`AADSTS50076`) — only for a **non-MFA service account**.

Gateway host: discovered at runtime. The portal hits a **global** host
(`projectmanagementservice.us-il101…`) `/api/ClusterDiscovery?regionName={region}&pageType=DW`, then
the **regional** host (e.g. `au-il102`) serving `/api/DualWriteManagement/...`. toolbAX's WebView2
capture was fixed to lock onto the regional (`DualWriteManagement`) host — already landed.

> Note: a `200 []` ("no cid") seen in live testing is **server-side** (the MS portal returns the same
> for the same request — likely a stopped/unlinked sandbox), not in scope here.

## Architecture

Introduce a profile-level **`AuthTarget.DataIntegrator`** credential, owned by the host's
profile/auth layer (not the plugin), with **two acquisition modes** behind one on-demand
token call:

- **ROPC mode** (browser-free; non-MFA service accounts): store username + password (DPAPI) +
  client id (default `2e49aa60`); acquire `IntegratorApp/.default` on demand via
  `AcquireTokenByUsernamePassword`, cached in-memory, re-acquired on expiry.
- **Interactive mode** (MFA users): the existing WebView2 portal capture yields an access + refresh
  token; store them in the profile (DPAPI); renew silently via the refresh-token POST
  (`DualWriteRefreshTokenProvider`, kept).

The plugin obtains a fresh token on demand from the host via a new context interface and never owns
credentials. The gateway URL + F&O identifier remain plugin-owned connection config.

## Components

### Core (`FoToolbox.Core`)
- `AuthTarget` enum → add `DataIntegrator = 2`.
- Constants: `DataIntegratorResourceBaseUrl = "https://IntegratorApp.com"`,
  `DataIntegratorDefaultClientId = "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b"`.
- New `DataIntegratorCredential` model (DPAPI-persisted) tagged by mode:
  - `Ropc { ClientId, TenantId, Username, PasswordRef }`
  - `Delegated { ClientId, AccessToken, RefreshToken, ExpiryUtc }`
- New `DataIntegratorTokenService` → `Task<string> GetTokenAsync(credential, ct)`:
  - ROPC → MSAL `AcquireTokenByUsernamePassword` (in-memory cache; re-acquire near expiry).
  - Delegated → reuse `DualWriteRefreshTokenProvider` to refresh, persisting the rotated token.
  - **Keep** `DualWriteRefreshTokenProvider`; it is required for the delegated path. (Earlier spec's
    "delete it" is wrong.)
- `DualWriteGatewayFactory` → add `CreateWithTokenProvider(string gatewayBaseUrl,
  Func<CancellationToken,Task<string>> getToken)` + a `DelegatedTokenHandler`. `DualWriteConnectionSettings`
  keeps gateway URL + identifier; token fields move out (the token comes from the credential).
- (Stretch) `ClusterDiscoveryClient` → resolve the regional gateway host from the global host using
  the IntegratorApp token, so the **ROPC path needs no browser at all** for discovery.

### SDK (`FoToolbox.SDK`)
```csharp
public interface IPluginContextDualWrite
{
    Task<string> AcquireDataIntegratorTokenAsync(CancellationToken cancellationToken = default);
}
```

### Host (`FoToolbox.Host`)
- `PluginContext` implements `IPluginContextDualWrite`: loads the active profile's `DataIntegrator`
  credential, calls `DataIntegratorTokenService`. Clear, actionable error if unconfigured or if ROPC
  fails on MFA ("This account requires MFA; use interactive sign-in for Data Integrator instead").
- `ProfilesView`/`ProfilesViewModel`: a "Data Integrator (dual-write)" section with a mode toggle —
  **ROPC** (username + password + client id, default `2e49aa60`) or **Interactive** ("Sign in with
  Microsoft", WebView2 capture). Persist via the DPAPI vault.

### Operations plugin (`plugins/DualWriteOperations`)
- Token via `ctx as IPluginContextDualWrite`; gateway via `CreateWithTokenProvider`.
- Keep **"Discover gateway"** (WebView2, API-host fix) for gateway-host discovery in interactive
  mode; for ROPC use `ClusterDiscoveryClient` (stretch) or manual gateway-URL entry.
- Connection store keeps gateway URL + identifier only. Keep the **"Switch account"** affordance.

## Data flow
- **ROPC:** Profiles → Data Integrator → enter service-account username/password (client `2e49aa60`).
  Plugin → `AcquireDataIntegratorTokenAsync()` → host ROPC → token → gateway calls.
- **Interactive:** Profiles → Data Integrator → Sign in (WebView2 capture: token + refresh). Plugin →
  `AcquireDataIntegratorTokenAsync()` → host refreshes via refresh token → gateway calls.
- Gateway host: discovered (ClusterDiscovery or WebView2) or entered; F&O identifier defaults from
  the active env.

## Error handling
- ROPC + MFA → `AADSTS50076`: surface "use interactive sign-in" guidance, don't loop.
- Not configured / token acquisition fails → actionable status pointing to Profiles → Data Integrator.
- Reuse `InteractiveSignInError.Describe` where relevant.

## Security
- ROPC stores a **user password** (DPAPI vault) — weaker than a token; gate it behind explicit
  opt-in and recommend a dedicated non-MFA service account. Interactive mode (token + refresh, no
  password) is preferred where MFA is in play.

## Testing
- Core: `DataIntegratorTokenService` ROPC vs delegated branches (fake MSAL / refresh provider);
  `CreateWithTokenProvider` attaches + refreshes; credential model round-trips through the vault.
- Host: `ProfilesViewModel` Data Integrator mode toggle + persistence; `AcquireDataIntegratorTokenAsync`
  resolves the right credential; default client id `2e49aa60`.
- Plugin: gateway built from context token; no credential owned by the plugin.

## Open questions / to verify during implementation
- **ROPC client id**: spec defaults to `2e49aa60` (self-authorized, no redirect needed). Not yet
  live-verified — validate with `artifacts/authprobe/probe.cs` (ROPC variant) before finalizing; the
  reference tool makes it configurable, implying the value may matter.
- **ClusterDiscovery** request/response shape (for browser-free discovery) — confirm against live
  traffic before committing to the stretch item.

## Out of scope / follow-ups
- Browser-free gateway discovery via `ClusterDiscovery` (stretch above) — would make ROPC fully
  headless.
- `DualWriteCompare` reusing the shared credential.
- In-app "Add plugin" capability (tracked separately).
- The server-side empty/`500` `Environments` result.
