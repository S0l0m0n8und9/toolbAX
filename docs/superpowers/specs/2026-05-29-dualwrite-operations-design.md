# Dual-write Operations — Design (v1)

Date: 2026-05-29
Status: Approved (auth/feature/packaging forks confirmed by user)

## Background

`microsoft/Dual-write-automations` automates the Dynamics 365 **Dual-write** setup. Its
real value is that it drives the **Dual-write Management gateway** — the same backend the
Power Platform admin "Dual-write" UI calls — to start/stop/pause maps, run initial sync,
apply map versions, refresh tables, apply integration keys, reset links and compare
environments.

toolbAX's existing `DualWriteMapBrowser` plugin only *reads* `msdyn_dualwriteentitymap`
records from the **Dataverse Web API** and validates FO↔CE row counts. It cannot *operate*
dual-write. That operational capability is the gap this work closes.

### Gateway API (reverse-engineered from `DWLibary`)

Base: `https://projectmanagementservice.{region}.gateway.prod.island.powerapps.com`
Path prefix: `/api/DualWriteManagement/1.0/`

| Capability | Method + path | Notes |
|---|---|---|
| Resolve environment | `GET Environments?targetType=AX&identifier={foEnv}` | → `cid`, `cname` |
| List maps + templates | `GET Entities?targetType=AX&cid={cid}` | maps carry `detail.pid`, active `template`, `detail.templates[]` |
| Start/Stop/Pause/Resume/Init | `POST Start` | action codes: 1=start, 4=stop, 5=pause, 6=resume, 8=initial-sync; body has `details[]` of `{tid,pid,cid}` |
| Poll request | `GET Status/{requestId}` | terminal state ends polling |

Auth in the MS tool is a **delegated user** token for the first-party Data Integrator app
(client `2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b`, scope `https://IntegratorApp.com/.default`).
A third-party app registration **cannot** mint app-only tokens for that resource.

## Decisions (locked)

1. **Auth: bearer now, interactive later.** v1 uses a user-pasted dual-write bearer token
   (captured from the portal's network tab). Interactive delegated MSAL + gateway-host
   auto-discovery is the documented follow-up.
2. **First feature: map lifecycle operations** — list maps + Start/Stop/Pause/Resume/
   Initial-sync with live status polling. Compare/Deploy/Export/Reset deferred.
3. **Packaging: shared Core gateway client + one plugin now.** The reusable, fully
   unit-tested API client lives in `FoToolbox.Core`; one WPF plugin consumes it.

## Architecture

### Layer 1 — `FoToolbox.Core/DualWrite/` (no UI, fully testable)

- `DualWriteGatewayClient(HttpClient http)` — `HttpClient.BaseAddress` is the gateway root
  (scheme+host); methods build relative URIs under `/api/DualWriteManagement/1.0/`:
  - `GetEnvironmentAsync(foIdentifier, ct)` → `DualWriteEnvironment`
  - `GetMapsAsync(cid, ct)` → `IReadOnlyList<DualWriteMap>`
  - `StartActionAsync(action, maps, cid, ct)` → `DualWriteActionResponse` (`requestId`)
  - `GetStatusAsync(requestId, ct)` → `DualWriteRequestStatus`
- `MapActionPayloadBuilder` — pure: turns (action, maps, cid) into the `Start` request body.
- Models: `DualWriteEnvironment`, `DualWriteMap`, `DualWriteTemplate`,
  `DualWriteActionType` (enum + `ToActionCode()`), `DualWriteActionResponse`,
  `DualWriteRequestStatus`.
- The bearer token is supplied by the caller's `HttpClient` handler — the Core client is
  auth-agnostic. Request construction is precise; response parsing is **tolerant**
  (defensive `JsonDocument` reads) because exact gateway JSON field names are
  reverse-engineered, not documented.

### Layer 2 — `plugins/DualWriteOperations/` (WPF, MVVM)

- `DualWriteOperationsPlugin : IFoToolPlugin` — id `fo.dualwriteoperations`, MinSdk `0.3.0`,
  capabilities `["DualWrite.Operate"]`.
- **Connection is plugin-owned** (mirrors `TestifyConfigurationStore`):
  `DualWriteConnectionStore` persists `{ gatewayBaseUrl, bearerToken }` per env to
  `%LocalAppData%/FoToolbox/dualwrite-connections.json`. The plugin builds its own
  `HttpClient` with a bearer-injecting handler from that config. This keeps host
  auth/profile schema untouched for v1.
- `DualWriteOperationsViewModel`:
  - Loads maps: `GetEnvironment(ctx.CurrentEnv identifier)` → `cid` → `GetMaps(cid)`.
  - DataGrid: name, direction, current template version/author, live state, checkbox select.
  - Commands: Start / Stop / Pause / Resume / Initial-sync over checked maps →
    `StartActionAsync` → poll `GetStatusAsync` until terminal → refresh state.
  - Every mutating command goes through a confirmation prompt; a persistent
    "⚠ Live environment" banner is always visible.
- `DualWriteOperationsView.xaml` — toolbar + DataGrid + status/connection panel, styled like
  the existing plugins.

### Data flow

Open → load connection config (prompt to set gateway URL + token if absent) → resolve env →
list maps → user checks maps + clicks action → confirm → POST Start → poll Status → update.

### Safety & errors

- Confirmation on every mutating action; Initial-sync warns it re-syncs data.
- Non-success gateway responses surface `(int)status + reason + trimmed body`
  (same shape as `DualWriteMapBrowserViewModel`).
- Token is treated as a secret: stored locally, never logged.

## Testing (TDD)

- `MapActionPayloadBuilder`: action-code mapping; `details[]` shape; init omits `pid`.
- `DualWriteGatewayClient`: each method's exact request URI, method, query, headers and
  (for Start) body, asserted via a fake `HttpMessageHandler`; response parsing against
  representative JSON.
- `DualWriteConnectionStore`: round-trip persistence + per-env isolation.
- Status-poll terminal-state logic.

## Out of scope (follow-ups)

- Interactive delegated MSAL + gateway-host auto-discovery (replaces pasted token).
- Apply map versions / refresh tables / integration keys (deployment).
- Environment compare; config export; reset links; ADO wiki upload.

## Honesty note on "hitting the API"

This environment has no live tenant/token, so a real gateway round-trip cannot be executed
here. Correctness is established by unit tests that assert the exact outbound request
(URL/method/query/headers/body) and parse representative responses. A live confirmation
requires a real environment + token and is part of the interactive-auth follow-up.
