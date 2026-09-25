# Dual-write Authentication Trust Boundary

**Status:** Core phases 1 and 2a implemented and locally validated, pending parent review. Overall H05 remains incomplete until the native App adapter supplies committed exchange/gateway evidence in phase 2b.
**Scope:** Offline endpoint and credential-origin enforcement for the Core dual-write sign-in capture, gateway factory and bearer handlers.

## Problem

The interactive capture currently recognizes gateway and token endpoints with substring searches. A lookalike host, path or query can therefore satisfy the marker. The gateway factory and bearer handlers also lack one explicit trusted origin, so an absolute request can receive a delegated bearer outside the captured gateway. Factory-owned HTTP transports follow redirects by default, which could move a credential-bearing gateway request or refresh form to another origin.

This phase establishes strict URI boundaries before any bearer attachment, token refresh, persistence callback or network dispatch. It does not determine whether a returned token belongs to the expected resource or tenant; that remains H05 phase 2.

## Accepted design

Add one narrow Core `DualWriteEndpointPolicy` helper. It parses and canonicalizes endpoints by URI components rather than marker substrings.

A trusted commercial gateway base must:

- be an absolute HTTPS URI using the default port 443;
- contain no user information, query or fragment;
- have the root path only;
- have `projectmanagementservice` as the exact first DNS label;
- end on the DNS-label boundary `gateway.prod.island.powerapps.com`, allowing one or more regional/routing labels between the first label and that suffix.

The canonical trusted gateway is `https://{lowercase-idn-host}/`. Explicit `:443`, uppercase spelling and a trailing slash normalize to the same origin. Non-root paths and non-default ports are rejected rather than silently discarded.

Browser capture accepts a gateway request only when the origin passes the same gateway-host policy and the decoded request path is `/api/DualWriteManagement/` or below it on a segment boundary. Query parameters on that API request are allowed. A gateway-looking fallback request may use another path, but its origin must still satisfy the same host policy. Captured results store the canonical trusted root.

An Entra token endpoint must:

- be an absolute HTTPS URI using the default port 443;
- use the exact host `login.microsoftonline.com` or `login.microsoft.com`;
- contain no user information or fragment;
- have exactly `/{tenant}/oauth2/v2.0/token`, where tenant is one nonempty decoded path segment;
- contain no encoded separator or backslash in any path segment.

Normal query parameters are allowed because host and path remain exact. Extra segments, lookalike hosts and marker text in a query are rejected.

`DualWriteGatewayFactory.Create` and `CreateRefreshing` validate and canonicalize the gateway before constructing a bearer client. The static and refreshing bearer handlers require that validated origin in their constructors. Each handler rejects a relative, downgraded, alternate-port or foreign-origin request before attaching credentials; the refreshing handler rejects it before expiry refresh and before the persistence callback. The existing single refresh and replay after a gateway 401 remains unchanged.

Factory-owned gateway and refresh transports use `HttpClientHandler.AllowAutoRedirect = false`. Tests and other callers may inject their own inner transports; those callers own redirect behavior. The code does not claim to control redirects inside an arbitrary injected handler.

## Phase boundary

Phase 1 does not decode access JWTs, require JWT-shaped access tokens, validate signatures or change MSAL auth. Phase 2a binds Core capture and refresh to committed request/response provenance while keeping access tokens opaque. Phase 2b must supply that evidence from the native browser adapter; it must not weaken the Core boundary or treat client-side JWT decoding as signature validation.

## Failure pre-mortem

1. **Lookalike gateway receives a bearer.** A hostname such as `projectmanagementservice.evil.example` or `projectmanagementservice.region.gateway.prod.island.powerapps.com.evil.example` passes a substring check. Mitigation: exact first label plus suffix on a DNS-label boundary, then bind handlers to the canonical origin.
2. **Token endpoint marker appears in attacker-controlled URI text.** A query or lookalike host contains `login.microsoftonline.com/.../token`. Mitigation: parse URI components and require exact host, port and path segments before observing a token body.
3. **Absolute request bypasses the factory base address.** `HttpClient` accepts an absolute URI and sends the bearer elsewhere. Mitigation: both bearer handlers reject every request outside their explicit validated gateway origin before credential attachment or refresh.
4. **Redirect moves credentials or refresh form off origin.** A factory-owned handler follows a 30x to another host. Mitigation: disable automatic redirects on every factory-owned gateway and refresh transport; injected transports retain explicit caller responsibility.
5. **A valid regional gateway is rejected.** Overly rigid host matching accepts only one observed regional shape. Mitigation: allow arbitrary nonempty DNS labels between exact `projectmanagementservice` and the fixed suffix, with positive tests for regional/routing forms, uppercase spelling and explicit default port.

## Acceptance evidence required

- Table-driven endpoint-policy tests cover trusted regional forms and canonical equivalents, plus evil prefix/suffix, user information, non-default port, non-root base path, query and fragment cases.
- Sign-in capture tests prove API path segment matching, trusted fallback behavior, exact token endpoint parsing and rejection of encoded separators/backslashes and query/host bait.
- Static and refreshing handler tests prove a foreign or downgraded request sends nothing, attaches no Authorization header and performs zero refresh/persistence calls; a correct origin receives the expected bearer.
- Factory tests prove invalid settings fail before gateway construction and factory-owned gateway and refresh transports have automatic redirects disabled.
- Focused Core tests run with `CI=true` in Release. Validation uses only fake handlers and makes no tenant, browser, gateway or public endpoint calls.

## Local validation

The original implementation produced a completed behavioral RED with 19/19 failures: every malicious endpoint and both unbound bearer paths reached the old permissive behavior. After the phase-one change, the endpoint/capture/refresh-handler set passed 59/59 and the then-existing Core `Category=DualWrite` set passed 135/135 under `CI=true` Release. Parent review added literal-backslash and user-information/fragment consistency cases: 5/45 failed before the correction and the endpoint-policy class passed 45/45 afterward. With that class included in the category, the final Core DualWrite set passed 180/180. Evidence is retained under `artifacts/h05/`. All transports were fake; no live sign-in or endpoint was contacted.

## Phase 2a: accepted Core provenance and binding design

Phase 2a adds a small immutable delegated binding containing the actual tenant GUID, exact first-party client ID, IntegratorApp resource and canonical requested scope context. The access token remains opaque. Neither capture nor refresh requires or decodes access-token JWT claims. Optional binding metadata travels with `DualWriteToken` and `DualWriteConnectionSettings`; a legacy refresh token without that provenance fails before network access with re-sign-in guidance.

Trusted sign-in capture consumes one committed HTTPS token-exchange observation: actual request URI, method, content type and form plus response status and body. It accepts only a form-urlencoded POST to a phase-one trusted endpoint with a 2xx response, the exact `DualWriteAuthConstants.ClientId`, an `authorization_code` or `refresh_token` grant carrying its required credential, and at least one scope under exact `https://IntegratorApp.com/`. Only `openid`, `profile` and `offline_access` may accompany the IntegratorApp scopes. Duplicate security-critical form fields, mixed or foreign resource scopes, a conflicting response scope, non-Bearer token type, nonpositive expiry, malformed JSON and failed responses are rejected with static diagnostics that contain no codes, refresh tokens, access tokens, bodies or identifiers. The old body-only capture method remains source-compatible but never accepts a token; the native App adapter must supply committed request/response evidence in phase 2b.

The tenant constraint accepts an explicit GUID or the intentional organizational aliases blank, `common` and `organizations`. A DNS domain constraint is resolved only through `https://login.microsoftonline.com/{escaped-domain}/v2.0/.well-known/openid-configuration`, using a fixed host, no redirects in the owned transport, bounded timeout and response size, cancellation, and an exact Entra issuer that yields a concrete tenant GUID. Domain input is validated as DNS labels and never controls the authority host.

For an explicit GUID endpoint, that committed exchange context is authoritative. Alias and domain endpoints require tenant evidence from the ID token returned in the same trusted response. If an ID token is present for any constraint, its payload must be a well-formed object whose `tid`, issuer, audience/authorized-party and time claims agree with the expected client, direct exchange endpoint and clock. The target `tid` and issuer drive the binding; `client_info.utid` is ignored because a guest user's home tenant can differ. This is consistency validation of metadata received directly over the trusted TLS response, not independent JWT signature validation, arbitrary-token authentication, or a claim of complete portal nonce/signature validation.

Capture completes only after the same opaque bearer is observed in an Authorization header on a 2xx response from a phase-one trusted DualWriteManagement API request. Token exchanges and gateway responses may arrive in either order. Small bounded pending sets correlate them without letting unrelated portal traffic or an invalid first token poison a later valid exchange. The canonical gateway origin is pinned only by the correlated response. URL-only observation and manual close never produce a best-effort result.

Refresh uses the captured actual tenant GUID endpoint plus the immutable client, resource, scope and known redirect context. It never falls back to `common` and never upgrades an unprovenanced legacy refresh token. The response may omit scope and ID-token metadata because the request is already pinned; when either is present it must remain consistent. A failed or inconsistent refresh does not replace the token, invoke persistence, or trigger replay. Legitimately omitted rotated refresh tokens retain the prior one. The existing one-401 replay, concurrency guard and no-extra-mutation-retry behavior remain unchanged.

The native WebView2 adapter is outside phase 2a. Its pinned XML event provides the committed network request/response needed by the new Core observation, but extracting request form, response status/body and gateway response Authorization remains phase 2b. Until that is wired, interactive App sign-in deliberately cannot complete through the legacy body/URL methods. Overall H05 remains incomplete.

### Phase 2a failure pre-mortem

1. **An unrelated first portal token poisons capture.** Validate trusted request provenance, exact client/grant/resource scope and response metadata before adding a bounded candidate; invalid candidates do not replace or block later valid ones.
2. **A guest user's home tenant is mistaken for the target tenant.** Bind `tid` and the exact issuer from the direct token response, ignore `client_info.utid`, and compare explicit/domain constraints to the target tenant.
3. **Network events arrive in the opposite order or body reading is delayed.** Maintain bounded token and gateway observations and correlate the exact opaque bearer in either arrival order.
4. **Manual close or cached URL state bypasses binding.** URL-only observation never completes capture and `BestEffortResult` is null until full bearer/gateway correlation succeeds.
5. **Refresh widens authority after capture.** Build the refresh endpoint from the immutable actual tenant GUID and require the captured client/resource/scope binding; missing legacy context fails before network or persistence.

### Phase 2a evidence boundary

Tests must cover opaque-token success, invalid-before-valid capture, both event orders, bearer mismatch, failed/malformed/duplicate exchanges, explicit/domain/alias tenant constraints, guest target-versus-home metadata, malformed/expired/wrong tenant/client/issuer ID metadata, bounded pending state, no close fallback, pinned refresh, invalid-refresh zero callback/replay, legacy zero-network refusal, cancellation and redirect controls. All tests use fake HTTP handlers. No tenant discovery, sign-in, browser, gateway or public endpoint is contacted.

Sources: [Microsoft access-token guidance](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens), [Microsoft OIDC protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols-oidc), [OpenID Connect Core ID Token validation](https://openid.net/specs/openid-connect-core-1_0.html#IDTokenValidation).

### Phase 2a local validation

The legacy body/URL trust path produced a completed behavioral RED with 2/2 failures before implementation. After the Core binding work, the focused trust/capture/resolver/provider/handler set passed 106/106 and the broader Core `Category=DualWrite` set passed 219/219 under `CI=true` Release. Both Core target frameworks and the source-compatible App project built with zero warnings and errors. Evidence is retained as `h05-phase2a-legacy-red`, `h05-phase2a-focused-green`, `h05-phase2a-dualwrite-green`, `h05-phase2a-core-build` and `h05-phase2a-app-compat-build` under `artifacts/h05/`. All HTTP transports were fake; no live tenant discovery, browser, sign-in, gateway or public endpoint was used.
