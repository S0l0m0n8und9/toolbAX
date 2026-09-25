# Environment Consistency — Design

Date: 2026-09-25  
Status: Proposed for parent review  
Campaign item: H01, with the `CoreProfileStore.ActiveId` ordering portion of H09

## Problem

The shell treats a profile id as the environment boundary even though a profile can be edited in place. A profile can keep its id while its F&O endpoint, Dataverse endpoint, tenant, client ids, auth modes, or default company changes. Existing id-only caches, sessions, loaded-data stamps, and post-await checks can then accept data or actions from the old environment as current.

The switch flow also commits the new header and persisted `ActiveId` before asking whether open tool state may be discarded. Declining the current prompt keeps old actionable tool state under a new header. Profiles' **Set active** path writes `ActiveId` itself and then fire-and-forgets a second shell switch, so it does not share one transactional decision.

H01 makes environment identity explicit, routes activation and active-profile identity changes through one awaited shell decision, and makes every affected asynchronous boundary discard or refuse stale work.

## Verified source findings

- `ShellViewModel.ApplyActiveEnvironmentSwitchAsync` currently assigns `ActiveEnvironment` and writes `_profileStore.ActiveId` before showing an optional refresh prompt. A declined or failed prompt leaves the new environment active while cached tools remain from the old one.
- `ProfilesViewModel.SetActive` writes its own `ActiveId` and `_store.ActiveId`, then raises an `Action<string>` that the shell handles fire-and-forget.
- `CoreProfileStore.ActiveId` changes `_activeId` before persistence; a failed SQLite write therefore leaves its in-memory value ahead of durable state.
- `CoreMetadataService` keys and commits by profile id. Its post-await check compares only `_cacheEnvId`; if no getter runs during a switch, the cache stamp never changes and an old result can commit.
- `DualWriteSession`, `DualWriteOpsViewModel`, `DualWriteMapViewModel`, and `VirtualTablesViewModel` use profile ids as their environment stamp. Same-id endpoint/auth edits are invisible.
- `CoreDualWriteMapReader` has a private id-plus-Dataverse-URL string identity for one cache. It does not cover the complete identity and duplicates normalization rules.
- `CoreODataClient` and `CoreDataverseClient` capture an `EnvProfile` for a call, but do not recheck the current identity after asynchronous token acquisition and before dispatch.
- Query and POST view models have no active-environment accessor. POST reads method/path/body/headers again after confirmation instead of sending the exact request that was approved.
- Only `DualWriteOpsViewModel` is currently disposable. Its in-flight connect can return after disposal and assign a new gateway session to the discarded VM.
- `CoreDualWriteConnector` does not use `DataIntegratorClientId`, `DataIntegratorMode`, or `DualWriteGatewayUrl`: it signs in through the portal and uses the gateway host discovered by that sign-in. Those legacy profile values therefore do not affect a current live session and are not part of H01 identity. H07 owns removal/legacy handling; H01 does not change auth-mode support.

## Environment identity

Add one immutable App-services record, `EnvironmentIdentity`, derived from one immutable `EnvProfile` snapshot.

It contains:

- profile id;
- normalized F&O resource base URL;
- normalized Dataverse resource base URL;
- tenant;
- F&O client id and auth mode;
- Dataverse client id and auth mode; and
- default company (`Legal`).

`EnvironmentIdentity.Create(EnvProfile)` uses `FoToolbox.Core.Auth.ResourceUrlNormalizer` for both endpoints, then canonicalizes only the URI scheme and authority. URL path, query, and fragment casing remains significant; if the normalized text is not a valid absolute URI, it remains exact. Profile id is an opaque SQLite key and remains exact with ordinal record equality. Tenant and client identifiers are trimmed and case-normalized because their GUID/domain semantics are case-insensitive. Default company (`Legal`) remains exact with ordinal equality; H01 has no source evidence that every stored company value is case-insensitive. Name, status, latency, and tier are intentionally excluded.

The helper accepts a real profile, never `null`. Callers that have an optional active profile explicitly branch on absence. In a production-bound path, a missing identity blocks the operation; it is never treated as permission to send. Optional active-environment constructor seams may remain for isolated tests, but `ShellViewModel.ResolveContent` must bind Query and POST as well as the already-bound Map, Virtual Tables, and Operations VMs. App wiring continues to bind Core metadata/HTTP services. Compare uses explicit source/target profiles and gains discard/cancel lifecycle only; H06c owns result attribution to those selections.

Every operation captures `EnvProfile` once at entry, then derives both `EnvironmentIdentity` and request/catalog inputs from that same immutable object. It must not call `_activeEnv()` separately to derive a stamp and an endpoint. Post-await checks compare the captured identity with a newly derived current identity.

## Activation transaction

The shell owns one awaited activation funnel used by the header and Profiles screen. A narrow `SemaphoreSlim` serializes overlapping activation and active-identity-save decisions; it is local coordination, not a new event framework.

1. Capture the current environment and identities.
2. Treat the same complete identity as a no-op. A first selection with no open data tools also needs no discard prompt.
3. Refuse the change with a clear status if POST or dual-write mutation is in its confirmation, authentication, submit, poll, refresh, or debug-write phase. The same guard applies before deleting the active profile, because deletion also disposes its tools.
4. If any data-tool VM has been opened, ask whether to discard its state before changing the store or header.
5. After every confirmation/approval await, while still holding the transition gate, recheck the previous/current identity and mutation state. A mutation that began while the dialog was open rejects the transition before persistence or disposal.
6. On decline or dialog error, keep the old environment, persisted `ActiveId`, tools, and drafts. Raise `PropertyChanged` for `ActiveEnvironment` even though its reference did not change, forcing the OneWay ComboBox back to the old value.
7. On acceptance, persist `_profileStore.ActiveId` first. A persistence failure leaves both shell and store on the old environment and forces the same ComboBox rollback.
8. Assign `ActiveEnvironment`, invalidate the environment-scoped metadata cache, then cancel/dispose every cached data-tool VM and rebuild only the currently displayed tool.

There is no state in which the new header coexists with old actionable tool content. Cosmetic replacement of the active profile updates the header/home subtitle but preserves tool instances and metadata caches.

Profiles receives the shell activation callback and awaits it. It does not pre-write `ActiveId`. Its local `ActiveId` and status change only after the shell reports success. Standalone tests may use an explicit local fallback callback, while production shell wiring is always the shared funnel.

## Active-profile save transaction

`ProfilesViewModel.Save` becomes asynchronous and captures the selected immutable profile plus every draft value before its first await. For an active identity change, the injected Shell callback owns approval, the post-approval recheck, persistence, active-record replacement, and tool invalidation while holding the transition gate; this prevents another Shell transition from landing between approval and persistence.

- If the selected profile is not active, or the complete identity is unchanged, persist normally. Cosmetic name/status/latency/tier edits preserve open tools.
- If an active profile's identity changes, ask the injected shell approval callback before persistence when open tool state exists.
- Reject an identity-changing save while a POST or dual-write mutation is in progress, even when no open read-only tool otherwise needs a discard prompt.
- A declined/failed approval leaves the stored/list profile unchanged and retains the user's drafts for correction or retry.
- After approval, Shell rechecks mutation state and that the captured pre-save identity is still active, then persists the captured updated record and invalidates under the same transition gate. Only after that callback succeeds may Profiles replace its list/selected record and raise `ProfileSaved`. A user selection change while approval is pending cannot relabel the result; only the captured profile id is updated, and `Selected` is replaced only if it still refers to that profile.
- When the active identity changed successfully, Shell updates the active immutable profile, invalidates metadata, and unconditionally disposes/rebuilds affected tools. A cosmetic save updates the profile reference without invalidation.

The existing best-effort old-auth-session eviction remains after successful persistence and uses the captured original profile.

## Request and cache boundaries

### Metadata

`IMetadataService` gains an explicit `Invalidate()` contract. `CoreMetadataService` replaces `_cacheEnvId` with full `EnvironmentIdentity` plus a monotonically increasing cache generation; its implementation clears all caches and increments the generation under the existing lock. Cacheless fakes implement an explicit no-op and wrappers delegate. Shell calls its existing `IMetadataService.Invalidate()` on every accepted switch, active identity save, or active-profile deletion, so no cache-owning implementation can be silently skipped.

Each load captures one profile, its identity, the derived `FoEnvironment`, and the generation under the cache lock. A result commits only when both generation and current complete identity still match. This rejects same-id edits, A-B-A transitions, and a late completion even when nobody called a getter during the switch.

### HTTP clients

`CoreODataClient` and `CoreDataverseClient` capture one profile and identity at entry. They build every URL from that profile. After token acquisition they compare the current complete identity before constructing/sending the request. A mismatch returns a non-success "environment changed" response without calling `HttpClient`; it never rebuilds against the new environment. Existing same-origin guards remain in force.

### Query and POST

Shell passes its active-environment accessor to Query and POST. Query captures identity, generation, path, and selected output shape before each run/load-more/export. It rechecks identity/generation/disposal before committing rows or opening a save picker.

POST captures identity and the exact approved method, path, body, and headers before confirmation. It marks the mutation active before the confirmation await and clears it only when the whole send flow finishes. It rechecks identity after confirmation, relies on the client check after token acquisition, and rechecks before displaying the response. A changed identity produces no dispatch and no response attributed to the new environment.

### Dual-write

`DualWriteSession` carries the captured immutable `EnvProfile` and derived `EnvironmentIdentity`; `EnvId` remains a convenience property. Real and fake connectors populate the snapshot.

Operations compares the session identity with the current full identity at every existing guard. Connect/load also uses a local generation and disposed flag. A session returned after invalidation is disposed before assignment. Lifecycle actions and debug toggles expose a mutation-in-progress flag covering their confirmation and all network phases, allowing Shell to reject a switch rather than disposing an accepted write.

`CoreDualWriteMapReader` reuses `EnvironmentIdentity` instead of its private string recipe. Multi-page loads pin the Dataverse API base derived from their single captured profile and stop/discard when current identity changes.

Map Browser and Virtual Tables stamp successful results with the complete loaded identity and captured profile. Count guards compare the complete identity. Retained loaded data builds links from its captured profile, never from the newly active environment. Once Shell invalidates/disposes that VM, its open/copy commands refuse to act, so a disposed stale screen cannot launch even its captured link.

## Lifecycle and cancellation

Affected cached VMs implement straightforward `IDisposable` where they do not already. Disposal sets a flag, increments a local generation, cancels generated read/load/export commands, and disposes the `EntityCatalogLoader` where present. Post-await code checks generation/disposal before changing observable state.

This applies to Query, POST read initialization, Metadata, Map Browser, Virtual Tables, Compare, and Operations connect/load. It prevents a discarded VM from resurrecting data, opening a late export picker, or retaining a gateway returned after disposal. It does not introduce an event bus, service locator, common base VM, or dispatcher abstraction.

## Failure pre-mortem

| Failure mechanism | User-visible failure | Mitigation and deterministic proof |
|---|---|---|
| Same profile id survives an endpoint/auth edit | Old metadata/session is accepted against a newly repointed profile. | Compare complete `EnvironmentIdentity`; invalidate metadata and tool sessions after a confirmed save. Table-driven identity tests cover same-id F&O URL, Dataverse URL, tenant, both client ids/modes, and company changes; cosmetic fields remain equal. |
| Cancelled switch has already changed persistence/header | Header shows B while store or tools remain on A; ComboBox stays on the rejected choice. | Confirm before persistence/header assignment; use one awaited activation funnel; explicitly notify `ActiveEnvironment` on rejection. Headless switcher tests cover decline, dialog error, and failure-injected store persistence. |
| Token acquisition completes after context changes | The client correctly pins A's token and A's URL, but can still dispatch to old A after the UI context has moved to B. | Capture A profile/identity and URL once; after token acquisition refuse dispatch unless current complete identity still matches. Gated-auth client tests assert the HTTP handler receives zero requests after a switch. |
| Slow A result arrives after B or A-B-A | A metadata/results overwrite the current B/A generation. | Pair complete identity checks with monotonic generation/disposed checks. Controlled `TaskCompletionSource` tests complete requests out of order, including A-B-A and metadata completion without an intervening getter. |
| Discard disposes a live mutation, or a late connect leaks/revives a session | Accepted POST/gateway action is interrupted ambiguously, or discarded Ops VM regains a live gateway. | Shell refuses switches, active identity saves, and active-profile deletion while mutation flags are set; read commands cancel on discard; Ops disposes any gateway acquired after generation/disposal changed. Tests hold confirmation/connect on gates and assert refusal or disposal. |

## Acceptance cases

- Same-id F&O URL, Dataverse URL, tenant, F&O client/mode, Dataverse client/mode, and company changes are identity changes.
- Rename, status, latency, and tier changes preserve identity and open tool instances.
- Declined header switch, Profiles switch, and active-profile save change neither header, persisted choice/profile, drafts, nor tools; the rendered ComboBox visibly rolls back.
- Active-id persistence failure keeps Shell and store on the previous environment.
- Header and Profiles activation invoke the same awaited shell funnel.
- A query started under A cannot commit after an accepted switch.
- A POST held in confirmation or token acquisition cannot dispatch after identity changes, and it sends the exact approved snapshot when unchanged.
- Header/Profile changes are refused while POST, dual-write lifecycle, or dual-write debug mutation is active.
- Same-id profile edits block stale gateway lifecycle actions, debug requests, map counts, and links.
- Map and Virtual Tables links remain attributed to the loaded environment.
- A metadata result cannot commit after a switch even when no getter observed the switch.
- A-B-A cannot commit an older generation.
- Disposal prevents a late export picker and disposes a gateway obtained after disposal.
- Existing origin-guard, cancellation, disposal-on-failure, and H02 sequential-sign-in tests remain green.

## Validation boundary

All proof is deterministic and local: xUnit, headless Avalonia, controlled task gates, fake HTTP handlers, and the two complete CI-strict Release solution gates. No Dataverse, F&O, gateway, portal, or tenant sign-in is used.

## Out of scope

- H03 uncertain write outcomes and reconciliation.
- H07 removal/legacy handling of unsupported auth modes.
- H09 aggregate atomic/asynchronous profile and secret persistence beyond the `ActiveId` cache-order correction.
- HTTP retry/throttling, parser policy, Compare service H02 sequencing, and UI visual redesign.
