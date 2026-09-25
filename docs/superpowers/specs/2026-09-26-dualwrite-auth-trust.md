# Dual-write Authentication Trust Boundary

**Status:** Phase 1 implemented and locally validated, pending parent review. Overall H05 remains incomplete until phase 2 binds token-response provenance, resource and tenant.
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

Phase 1 does not decode access JWTs, require JWT-shaped access tokens, validate signatures, change MSAL auth, or decide token audience/tenant provenance. Phase 2 must bind the refresh response and captured sign-in response to the expected resource, tenant and request context using trusted response metadata and provider behavior. It must not treat client-side JWT decoding as signature validation.

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
