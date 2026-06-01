# Interactive Microsoft sign-in (FO / CE)

On the **Profiles → Auth** tab, when an environment's **Auth mode** is **BearerToken**, there are
two ways to obtain a token:

- **Get token (Azure CLI)** — runs `az account get-access-token` (requires `az login`).
- **Sign in with Microsoft…** — an interactive delegated sign-in that needs no Azure CLI.

This document covers the interactive route.

## How it works

The app uses MSAL's public-client interactive flow via the **system browser** with a
**loopback redirect** (`http://localhost`). MSAL starts a temporary local listener, opens your
default browser to sign in, and captures the resulting delegated access token. The token is then
stored (DPAPI-encrypted) and used like any other bearer token for that environment.

The MSAL **token cache is persisted** (DPAPI, per environment under
`%LocalAppData%\FoToolbox\msal-cache`). After the first successful sign-in, subsequent acquisitions
renew **silently** from the cached refresh token — no browser prompt — across operations and app
restarts, until the refresh token itself expires.

## App registration prerequisite

Interactive sign-in only works if the Entra **app registration** referenced by the environment's
**Client ID** is configured as a **public client** with a loopback redirect:

1. In the Entra admin center, open the app registration → **Authentication**.
2. **Add a platform → Mobile and desktop applications**.
3. Add the redirect URI **`http://localhost`**.
4. Under **Advanced settings**, set **Allow public client flows** to **Yes**.
5. Ensure the signing-in user (or admin) has consented to the delegated permissions the target
   API requires (F&O / Dataverse).

If these aren't configured, sign-in opens the browser but fails at the redirect. The app surfaces
an actionable message for the common cases (redirect-URI mismatch, "public client required"); see
`InteractiveSignInError`.

## Switching accounts

Because sign-in is **silent-first**, once a session is cached for an environment, clicking
**Sign in with Microsoft…** renews that same account silently and does **not** reopen the browser.
To sign in as a **different** Azure AD principal, clear the cached session first by deleting the
cache directory for that environment:

```
%LocalAppData%\FoToolbox\msal-cache
```

(A dedicated "switch account / sign out" action is a candidate future enhancement.)

## Notes / limitations

- The Azure CLI route is unchanged and remains available for users who prefer it.
- Silent renewal happens when you next acquire a token (e.g. click **Sign in with Microsoft…**
  again, or re-apply the profile). Fully automatic background refresh on token expiry is a
  potential future enhancement.
