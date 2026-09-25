# Environment Consistency — Design

Date: 2026-09-25  
Status: Accepted for implementation; H01 in progress  
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
- Clearing `CoreMetadataService`'s front cache is insufficient for tenant/client/mode/company edits: `ResolveEnv` still supplies raw profile id and `CatalogService` persists metadata under id+URL, so `UseCacheIfFresh` can immediately rehydrate metadata obtained under the old auth/company context. That persistent cache survives a new App service instance.
- `CoreAuthService` app-only F&O/Dataverse acquisition looks up a service principal by `env.Id` after an await and then trusts the returned row. It does not verify the row still belongs to the captured environment id/target/client id/auth mode, so an A-B-A transition can hand B's persisted principal to A's broker call.
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

#### Locked design addendum: persistent catalog partition

`FoToolbox.Core.Models.FoEnvironment` gains one optional init-only `[JsonIgnore] string? MetadataCachePartition`. It does not alter `Id`, request routing, authentication, or the SQL profile schema. `CoreMetadataService` derives the value from the same single captured `EnvironmentIdentity` used for its front-cache generation and attaches it to the captured `FoEnvironment`.

The partition is deterministic and explicitly versioned. Serialize the identity fields in a fixed declared order using field names plus UTF-8 byte lengths and bytes; serialize auth enums with invariant numeric values. Hash those bytes with SHA-256 and emit a stable value such as `envmeta-v1:<lowercase hex>`. Do not use `GetHashCode()`, record/object `ToString()`, runtime-dependent JSON defaults, or a delimiter-only concatenation. Normalized endpoint aliases and cosmetic profile edits therefore reuse a partition; profile id, case-sensitive URL suffix, tenant/client/mode, and exact company changes produce a different partition.

`CatalogService` separates table identity from metadata identity:

- Tables, user imports, and the H08b Tables-only `UserImport` migration keep their existing key and migration rules unchanged.
- When `MetadataCachePartition` is present, every OData metadata representation uses the partitioned metadata key: parsed metadata, entity index, entity details, raw XML, in-memory XML memo, ETag/max-age records, and per-key load locks.
- A partitioned lookup never falls back to an unpartitioned or different-partition metadata entry.
- A legacy Core caller that leaves `MetadataCachePartition` unset retains the existing catalog-v2 id+URL behavior.

All three Catalog HTTP request-construction sites tag the request with the same immutable partition using one small public Core `HttpRequestOptionsKey<string>` owned by the Catalog boundary. App `AuthenticatedHttpHandler` treats that tag as an authenticated environment-bound request: before any token acquisition it captures the active profile, derives its full identity/expected partition, rejects a missing or mismatched active identity, and applies the existing same-origin rule. A tagged request cannot bypass this gate through an anonymous marker or a pre-existing `Authorization` header. After token acquisition and before inner-handler dispatch, it re-derives the current full identity/partition and rejects any drift. Untagged request behavior and existing origin protections remain unchanged.

This handler gate closes changes that land inside Catalog's own awaits. The App adapter's post-await generation still decides whether a successful response may update the UI, while the tagged handler guarantees no request is sent after its captured context becomes stale.

Acceptance uses the real `CatalogService` with a temporary `CatalogStore` and a fake HTTP handler serving distinct metadata documents with the same ETag. It must cover full metadata, entity index, entity details, and a warmed raw-XML memo; repeat the reads after constructing a fresh Catalog service over the same store; prove tenant/client/mode/company partitions are isolated; prove cosmetic and normalized endpoint aliases reuse; prove request URI and actual `FoEnvironment.Id` are unchanged; and prove Tables `UserImport` migration data remains present and independent.

### App-only principal binding

After each app-only F&O or Dataverse principal lookup, `CoreAuthService` validates the returned principal before invoking the token broker. The principal's environment id, target, normalized client id, and auth mode must match the single captured profile. Client-id normalization uses the same trim/case semantics as `EnvironmentIdentity`. A missing or mismatched principal fails closed with no broker call. A controlled A-B-A test gates principal lookup, returns B's row to A's captured request, and proves no token-acquisition delegate is invoked with B. Narrow injected principal-lookup and token-acquisition delegates, or an equivalent existing internal seam, are permitted solely to make this deterministic without live auth. Same-client secret rotation remains H09 and secrets do not enter `EnvironmentIdentity`.

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
| Same profile id survives an endpoint/auth edit | Old front-cache or persistent metadata/session is accepted against a newly repointed profile. | Compare complete `EnvironmentIdentity`, invalidate tool state, and attach the stable identity SHA-256 partition to every Catalog metadata layer and lock. Real-store restart tests use distinct same-ETag documents and prove no cross-partition fallback while Tables/imports remain unchanged. |
| Cancelled switch has already changed persistence/header | Header shows B while store or tools remain on A; ComboBox stays on the rejected choice. | Confirm before persistence/header assignment; use one awaited activation funnel; explicitly notify `ActiveEnvironment` on rejection. Headless switcher tests cover decline, dialog error, and failure-injected store persistence. |
| Token or principal lookup completes after context changes | The client can dispatch old-A catalog work after the UI moved to B, or B's looked-up app-only principal can reach A's broker during A-B-A. | Tagged Catalog requests must pass full identity/partition/origin checks before auth and again after token await; app-only principals must match captured env/target/normalized client/mode before broker invocation. Gated tests assert zero HTTP and zero wrong-principal broker calls. |
| Slow A result arrives after B or A-B-A | A metadata/results overwrite the current B/A generation or warm a cache later reused as current. | Pair adapter generation/disposal checks with partitioned persistent keys, XML memo, ETag state, and load locks. Controlled task gates plus real-store restart tests cover A-B-A and completion without an intervening getter. |
| Discard disposes a live mutation, or a late connect leaks/revives a session | Accepted POST/gateway action is interrupted ambiguously, or discarded Ops VM regains a live gateway. | Shell refuses switches, active identity saves, and active-profile deletion while mutation flags are set; read commands cancel on discard; Ops disposes any gateway acquired after generation/disposal changed. Tests hold confirmation/connect on gates and assert refusal or disposal. |

## Acceptance cases

- Same-id F&O URL, Dataverse URL, tenant, F&O client/mode, Dataverse client/mode, and company changes are identity changes.
- Rename, status, latency, and tier changes preserve identity and open tool instances.
- Declined header switch, Profiles switch, and active-profile save change neither header, persisted choice/profile, drafts, nor tools; the rendered ComboBox visibly rolls back.
- A pending switch re-resolves its exact target id after confirmation; target deletion or connection-identity edits refuse the switch, while a cosmetic same-identity replacement may supply the latest display record.
- Active-id persistence failure keeps Shell and store on the previous environment.
- Header and Profiles activation invoke the same awaited shell funnel.
- A query started under A cannot commit after an accepted switch.
- A POST held in confirmation or token acquisition cannot dispatch after identity changes, and it sends the exact approved snapshot when unchanged.
- Header/Profile changes are refused while POST, dual-write lifecycle, or dual-write debug mutation is active.
- Same-id profile edits block stale gateway lifecycle actions, debug requests, map counts, and links.
- Map and Virtual Tables links remain attributed to the loaded environment.
- A metadata result cannot commit after a switch even when no getter observed the switch.
- A same-id/same-URL tenant, client, auth-mode, or company change cannot reuse parsed/index/details/raw-XML metadata, warmed memo state, ETag freshness, or locks from the prior partition, including after service restart.
- Tagged Catalog requests dispatch zero HTTP when identity/partition changes before auth or during token acquisition; anonymous/pre-authorized tags do not bypass the gate.
- App-only principal lookup validates environment id, target, normalized client id, and auth mode; an A-B-A lookup returning B's principal invokes no token broker.
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
