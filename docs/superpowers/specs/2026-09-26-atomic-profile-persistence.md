# Atomic Profile Persistence — Phase 1 Core Design

**Status:** Core transaction, App facade and asynchronous UI integration phases are implemented and locally focused-validated. Parent source review and full App/Core gates remain pending.

## Problem and boundary

Environment, Dataverse, service-principal, settings/default and DPAPI vault rows share one SQLite database, but current public calls open separate connections and transactions. A late settings or credential failure can therefore leave a partially updated profile. Phase 1 adds one narrow typed transaction callback for these profile primitives without changing schema, encryption format, public legacy methods, saved-query/API-request storage, authentication policy or App behavior.

`ProfileStore` and `ProfileService` expose `RunProfileMutationAsync<T>(Func<ProfileMutationSession,T>, CancellationToken)`. The entire synchronous callback runs on `Task.Run`. It opens one connection and establishes a non-deferred SQLite write transaction before callback reads. Microsoft.Data.Sqlite async APIs execute synchronously, so the callback intentionally uses synchronous typed database operations inside the offloaded worker; it accepts no async/network work and contains no blocking wait on asynchronous APIs. Cancellation is checked before scheduling, before opening, at every session operation and immediately before commit. After commit succeeds, no cancellation check may turn success into a false rollback report.

`ProfileMutationSession` exposes only typed environment/Dataverse/service-principal/setting/default reads and mutations, insertion of an already-protected secret row, and reference-aware secret cleanup. It never exposes its connection, transaction or raw SQL. The session is invalid outside callback scope and cannot start nested transactions. Every command is parameterized and explicitly bound to the same connection and transaction. Unknown settings, unsupported/legacy principals and unrelated rows survive unless the callback explicitly changes them.

Secret deletion succeeds only when no surviving `ServicePrincipals.SecretRef` or `Settings.Value` references that ID. Shared references therefore survive a profile/target replacement. Environment deletion retains existing foreign-key behavior; callers explicitly request reference cleanup for any affected secret IDs within the same transaction.

`SecretVaultService.PrepareSecret<T>` performs the existing JSON/UTF-8/DPAPI CurrentUser protection before database mutation and returns an immutable protected row containing ID, kind and ciphertext only. It zeros the temporary UTF-8 plaintext byte array in `finally`; managed strings cannot be promised zeroed. `StoreSecretAsync` remains compatible and reuses preparation. Crypto failure happens before any transactional change. The protected row is readable by the existing vault reader after insertion.

No ambient/static transaction state, public raw connection, custom journal, distributed transaction, retry policy, WAL migration, machine key setup or new encryption format is introduced.

## Failure pre-mortem

1. **A late service-principal or settings failure leaves a partially changed profile.** One connection and write transaction encloses all callback reads/writes; deterministic late exceptions/triggers roll back environment, principal, setting, default and blob rows together.
2. **Blob insert, pointer update or old-blob cleanup fails midway.** Prepared ciphertext is inserted and referenced/cleaned within the same transaction; failure rolls every row back.
3. **Replacing one target deletes a secret still shared elsewhere.** Cleanup queries both surviving principal and setting references before deleting the vault row; shared references remain.
4. **Cancellation arriving after commit reports failure although data persisted.** Cancellation is checked immediately before commit and never after successful commit; the returned result matches database truth.
5. **Methods named async still block the UI because Microsoft.Data.Sqlite executes synchronously.** The complete open/transaction/callback/commit/rollback lifecycle runs on `Task.Run`; a gated callback proves the API returns control to its caller while work is held.

## Validation boundary

Tests use only real temporary SQLite databases and synthetic Windows DPAPI payloads. They cover historical partial-write behavior, late trigger/exception/cancellation rollback, success readback of every row/blob/default, shared-reference preservation, unrelated/legacy-row preservation, escaped-session refusal, pre-cancel no callback, pre-commit cancel rollback, post-commit cancellation truth and gated caller responsiveness. No user/default profile database, live service or machine configuration is accessed.

Source: [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

## Local phase-one validation

The historical separate-call control demonstrates partial state survives a late caller failure. A deliberate rollback-to-commit mutation then made all three late-exception, trigger-failure and pre-commit-cancellation rollback regressions fail behaviorally. After restoring the transaction boundary, the complete mutation suite passed 12/12 and the broader profile/schema/migration/vault set passed 23/23 under `CI=true` Release. Both Core target frameworks built with zero warnings/errors. Evidence is under `artifacts/h09/` (`h09-rollback-red`, `h09-green-final`, `h09-profile-vault-green-final`, `h09-core-build-final`). Tests used synthetic temporary databases and Windows DPAPI only; no user/default profile database or live service was accessed.

Core phase 1 is implemented and locally validated. App facade/cache publication and UI save/delete/secret-flow integration remain required before H09 can be complete.

Parent review added two callback-contract safeguards. `Task`, `Task<T>`, `ValueTask` and `ValueTask<T>` results are rejected before opening/invoking, and sessions now end scope immediately when the synchronous callback returns while enforcing their owning thread. A `RAISE(ROLLBACK)` control proves safe unwind preserves the original SQLite error. Behavioral RED failed the Task, ValueTask and cross-thread cases while the automatic-rollback control passed; GREEN passed 4/4. The final transaction set passed 16/16 and broader profile/schema/migration/vault passed 27/27; both Core targets built with zero warnings/errors. Evidence is `h09-contract-{red,green}`, `h09-green-review-final`, `h09-profile-vault-review-final` and `h09-core-build-review-final` under `artifacts/h09/`.

## Phase 2: accepted App service-facade design

`IProfileStore` and `ISecretStore` gain cancellable async mutation/read members with source-compatible synchronous members. Interface defaults may call existing synchronous fake implementations after a cancellation precheck. `CoreProfileStore` and `CoreSecretStore` override every async member with the offloaded Core transaction/read path. Their synchronous members remain compatibility wrappers and may block; production UI migration is phase 3, so H09 remains incomplete.

`CoreProfileStore.SaveAsync` reads the persisted environment, Dataverse row, both service principals and every known raw setting inside the same transaction that applies the aggregate. Validation and unsupported/raw-mode preservation derive from that current database snapshot, never a potentially stale App cache. Environment/Dataverse/principal/known-setting writes and superseded-secret cleanup commit or roll back together. Existing H07 mode/client semantics, DI blank/null behavior and deferred D02 casing semantics remain unchanged.

`DeleteAsync` captures candidate secret refs, removes the profile's principals and known settings (including the DI ref), deletes the environment, reference-aware cleans candidates, and clears `DefaultEnvId` only when its persisted value still equals the deleted profile. `SetActiveAsync` persists before publishing. One per-instance async gate serializes all three mutations through database work and cache/ActiveId publication. Cache reads are protected and `GetAll` returns a read-only snapshot. Before-commit failure/cancellation leaves cache untouched; after commit the known result is published without a fallible reread or post-commit cancellation check.

`CoreSecretStore.SetSecretAsync` validates plaintext/platform, prepares DPAPI ciphertext on the offloaded mutation worker, reads the current target pointer inside the transaction, inserts the new row, updates the principal or DI setting and reference-aware removes the old blob atomically. A missing F&O/Dataverse principal remains a no-op. `ClearSecretAsync` clears pointer and blob in one transaction. `HasSecretAsync` performs an offloaded reference-presence read without plaintext access or a global presence cache.

### Phase 2 failure pre-mortem

1. **A stale cached profile overwrites legacy database data.** Build validation/preservation from the transaction's persisted snapshot, then publish the committed requested profile only after success.
2. **A late credential or settings operation leaves a partial profile.** Keep environment, CE, both target principals, known settings and candidate cleanup in the single Core transaction.
3. **Two successful commits publish cache state out of order.** One async mutation gate covers database execution and cache/default publication.
4. **Secret rotation leaves an orphan, dangling pointer or deletes a shared ref.** Insert, pointer update and reference-aware old-row cleanup share the transaction.
5. **An async method still blocks the UI.** Real facades override async members with Core's offloaded worker; sync wrappers are compatibility-only until phase 3. Gated tests prove calls return while workers are held.

## Phase 2 local validation

The behavioral RED used the former interface defaults and separate database calls: all 3 controls failed by leaving a partial profile, delete, or secret rotation after a late failure. The atomic facade regressions now pass 15/15, covering late CE/principal/setting/blob/pointer/cleanup failures, cancellation, shared references, default and cache publication, immutable snapshots, Data Integrator pointers, and caller responsiveness. The broader `CoreProfileStoreTests`, `CoreSecretStoreTests`, and atomic facade set passes 72/72 under `CI=true` Release. Those SQLite fixtures share a collection because their legacy teardown calls process-global `SqliteConnection.ClearAllPools`; the initially parallel combined run reproduced a real cross-fixture handle-disposal race before serialization.

The App and Core projects both build under `CI=true` Release with zero warnings and errors. Evidence is under `artifacts/h09/app-phase2/`, including `h09-app-red`, `h09-app-profile-secret-green-postcleanup-serialized`, `h09-app-build-postcleanup` and `h09-core-build-postcleanup`. Tests used synthetic temporary SQLite databases and Windows DPAPI only; no user/default profile database or live service was accessed.

Parent reviewed the transaction boundary, reference-aware secret cleanup, serialized cache publication and legacy compatibility. The complete offline gates then passed: the `CI=true` Release App solution with `EnableWebView2=true` and the Core solution both built with zero warnings/errors; App passed 1,379/1,379 and Core passed 472/472 with no failures or skips. Evidence is `parent-full-{app,core}-{build,test}.log` and `parent-full-{app,core}.trx` under `artifacts/h09/app-phase2/`. H09 remains incomplete pending H08a integration, UI phase 3, publication, PR review and merge.

## Phase 3: locked asynchronous UI integration design

Phase 3 starts only after the merged H08a source is integrated. Its bounded production scope is `ProfilesViewModel` and `ProfilesView`, `ShellViewModel`, one small App `EnvironmentWriteGate` and delete-outcome model, the existing Post Builder and Dual-Write Operations mutation-admission hooks, the async `ISecretStore`/`CoreSecretStore` result refinement, and `CoreProfileStore` creation/offload documentation. Relevant tests, this specification and the H09 tracker row complete the scope. H03 transport, receipt and replay behavior, authentication policy, retries, paging, logging and unrelated hardening remain unchanged. Legacy synchronous facade APIs remain source-compatible; production UI mutation paths use the asynchronous APIs.

### Nonblocking persistence and UI publication

Add, Delete, Save, Set active and every secret Store/Clear command await the facade APIs. `ProfilesViewModel` owns one non-queuing persistence operation at a time. `IsPersisting` disables conflicting commands and editor inputs, while every execution path repeats the admission check so direct command invocation cannot bypass the UI. Before awaiting, each operation captures an immutable profile/id/name/auth context, the submitted plaintext when applicable, and editor/input revisions.

Profile lists, Shell state, notifications and success messages publish only after the corresponding commit succeeds. Failure or cancellation before commit leaves the prior UI state and stored aggregate intact. Facades retain the existing truth contract that cancellation after a successful commit cannot be reported as rollback; UI code must not add a post-await cancellation check that changes that result. Disposal cancels owned operations and suppresses late UI/event publication, while committed store truth remains durable for the next load.

A late completion must not erase newer selection, drafts or typed secrets, including same-id A-B-A edits. When a saved record replaces the selected record in the list, a narrow draft-reload suppression updates the selected record identity without overwriting drafts changed after submission. Input revisions ensure a successful secret store clears only the exact submitted plaintext still owned by that operation. Success and error messages use the captured profile name and target, never the selection that happens to be current at completion.

### Secret presence and async store outcomes

`HasSecret`, `HasDataverseSecret` and `HasDiSecret` become cached UI state and never perform synchronous database reads. One cancellable refresh command loads all target-presence values with `HasSecretAsync` for a captured selection snapshot. An epoch plus selection and disposal checks rejects stale completion, including A-B-A. Presence is modelled as loading, known present, known absent or failed/unknown so the UI never labels a pending or failed lookup as "no secret". The refresh command handles faults internally and exposes `ExecutionTask` for deterministic tests; it leaves no abandoned unobserved task.

H08a probe preflight awaits a fresh `HasSecretAsync` call under its existing profile snapshot, generation and caller-cancellation guards. It does not trust the display cache. Successful secret Store/Clear operations refresh the operation-owned presence state. A successful Clear may publish known absence only while the captured selection/epoch is still owned.

Only the new asynchronous `SetSecretAsync` contract changes, from `Task` to `Task<bool>`. The real transaction returns `true` only when the targeted principal or Data Integrator setting was updated with the committed protected secret; it returns `false` for the existing missing-principal no-op. The default synchronous-fake adapter calls `SetSecret` and then reports `HasSecret`; synchronous `SetSecret` stays compatible. UI clears the captured plaintext only for committed `true`. A `false` result preserves the typed input and shows the existing save-principal guidance without relying on a fallible post-commit presence verification. A newer input revision is never cleared.

Real `CoreProfileStore.CreateAsync` offloads the SQLite load lifecycle rather than synchronously loading before returning a completed task. Sync-wrapper comments must accurately say compatibility callers may block; no documentation may claim synchronous callers are nonblocking.

### Shared environment-write exclusion

Add one per-Shell `EnvironmentWriteGate` with lock-protected admission and idempotent `IDisposable` leases. It admits at most one profile-commit lease, and only while no live-write lease exists. Live-write leases are admitted only while no profile commit exists; existing simultaneous live writes do not need new serialization. There is no static/global gate, queue or retry framework.

Every profile, secret and default-environment database mutation holds the same profile-commit lease through database commit and coherent UI publication. Deliberate environment-switch and active-identity-save confirmations occur before acquiring the lease. The existing Shell transition semaphore, post-confirmation active-identity check, mutation check and target-store re-resolution remain, so edits or live writes that start while confirmation is open are still detected before commit.

`PostBuilderViewModel.BeginWriteOperation(mutation: true)` and `DualWriteOpsViewModel.TryBeginOperation(isMutation: true)` acquire a live-write lease for exactly the existing `MutationInProgress` lifetime and release it through every existing cleanup path. They refuse before dispatch when profile persistence owns the gate. Readback and other reads remain outside the live-write lease. Shell wires its Post Builder and both real- and test-factory Operations instances to the same gate before use; standalone default view models may run without a shared gate. Existing private command ownership remains unchanged. UI command availability reflects the shared busy state, but execution-time admission is authoritative. Tests cover both directions: live write blocks profile persistence, and awaited profile persistence blocks live-write dispatch, including all failure, cancellation and disposal release paths.

### Awaited deletion coordination

Replace the Shell's database-writing synchronous `ProfileDeleted` event handler with an awaited delete coordinator. The coordinator returns a small outcome containing `Deleted` plus an optional warning, so a committed deletion cannot later be described as rolled back.

Under one profile-commit lease, `DeleteAsync` first commits removal and default clearing. The coordinator then chooses a replacement from the surviving cached profiles and awaits `SetActiveAsync` before publishing the replacement to Shell. When this second commit succeeds, list, selection, Shell active environment and tool invalidation publish coherently. When replacement activation fails, the deleted profile remains removed, Shell active environment remains null, the store default remains cleared, tools tied to the deleted environment are invalidated, and the result reports `Deleted = true` with an explicit replacement-activation warning. No stale deleted identity and no async-void event handler is permitted. Other events remain post-commit notifications only. Deleting a non-active profile preserves the unrelated active environment and open tool state. The last-profile guard remains and captured inputs are rechecked where ownership could have changed.

### Phase 3 UI failure pre-mortem

1. **Synchronous database work freezes the UI.** Every production mutation and presence read awaits an offloaded facade path; gated responsiveness tests hold the worker and prove the caller regains control.
2. **A late commit erases a newer draft or typed secret.** Immutable submitted snapshots plus editor/input revisions and suppressed draft reload publish the committed record without overwriting newer user input.
3. **Stale presence labels the wrong profile.** Loading/unknown states plus selection epoch, disposal and captured-id checks discard late presence results, including A-B-A.
4. **An async profile commit overlaps a live write or header switch.** One Shell-scoped write gate covers profile/default/secret commits and the existing Post/Ops mutation lifetime in both admission directions, while confirmations and transition rechecks remain intact.
5. **Fallback activation failure falsely reports deletion rollback.** The delete outcome distinguishes the committed removal from a failed second activation commit and leaves the Shell/store in an explicit removed/null state with a warning.

Phase 3 retains H07 unsupported/raw-auth preservation and explicit legacy handling, H08a independent probe status/ownership and `LegacyDiStatus`, H01 confirmation/identity/tool-invalidation behavior, and H03 uncertain-write behavior.

### Phase 3 acceptance and validation boundary

Meaningful gated RED/GREEN covers every asynchronous command in pending, failure and success states; cancellation before and after commit; caller responsiveness; same-id draft A-B-A, selection changes and disposal; typed-input preservation and secret-store true/false outcomes; presence loading, failure and stale completion; fresh probe credential preflight; header activation and active-identity save; mutual exclusion with live Post and Operations writes in both directions; declined/failed changes without invalidation; truthful active-delete fallback failure with removed/null state; successful replacement activation; and non-active deletion preserving unrelated tools.

Integrated H08a behavior includes an Avalonia `ListBox` transient-null selection when a selected record is replaced in its `ObservableCollection`. Phase 3 asynchronous save publication must distinguish that internal same-ID replacement churn from a genuine newer user selection or draft revision. Tests retain the final selected/draft truth, preserve a still-owned same-profile confirmation, let real null/different selection win, and keep H08a generation/snapshot guards authoritative across the await.

Existing Profile, Shell, Post, Operations, render and atomic-facade tests remain in the focused gate. Tests use only fake transports and temporary SQLite/DPAPI fixtures. Focused `CI=true` Release validation runs first; parent reviews source before the full App/Core gates. No live service call, push or PR is part of phase 3 implementation or validation.

## Phase 3 local validation

The integrated H08a source was merged into the phase-2 branch before implementation. A labelled fault-injection RED restored newer-draft overwrite, missing-principal false success, stale presence publication and deletion false-rollback reporting; 3/10 async UI cases failed while 7 controls passed. Restoring the safeguards passed the async UI/write-gate/delete suite. A separate per-command gate then exposed Toolkit's deferred direct execution, so the nine persistence commands now use a private non-queuing command owner whose admission precedes cancellation-token allocation while direct calls still reach method-level refusal guidance.

Parent source review extended Shell's profile lease through active-identity publication and tool invalidation, made presence reads conditional on per-profile/target commit revisions, retained accepted Post/Ops live-write leases until their operation-finally paths drain, and suppressed post-disposal Shell publication while preserving committed store truth. Follow-up review gave each accepted manual presence refresh its own epoch and added a bound retry control for failed credential reads. Final lifetime review made disposed rejection a no-publication return and observes cancellation for a header command queued behind the transition semaphore. The bounded Shell/async correction suite passed 24/24.

The final `CI=true` Release App solution build with `EnableWebView2=true` passed with zero warnings/errors. The complete focused Profile, Shell, Post Builder, Dual-Write Operations, render, CoreProfileStore, CoreSecretStore and atomic-facade set passed 502/502 with zero failures/skips. Evidence is under `artifacts/h09/phase3/`, including `h09-phase3-behavioral-red-2.{log,trx}`, `h09-phase3-review-corrections-green.{log,trx}`, `h09-phase3-presence-followups-green.{log,trx}`, `h09-phase3-shell-lifetime-green.{log,trx}`, `h09-phase3-review3-app-build.log`, and `h09-phase3-review3-focused-green.{log,trx}`. Tests used fake transports and temporary SQLite/Windows DPAPI only; no user database or live service was accessed.

Parent accepted the final phase-3 source and independently ran the full offline gates. App passed 1,515/1,515 and Core passed 472/472 with zero failures/skips. The Core `CI=true` Release build passed with zero warnings/errors, and the final App `CI=true` Release build with WebView2 remained zero-warning/error. Evidence is `artifacts/h09/phase3/parent-full-app-test.log`, `parent-full-app.trx`, `parent-full-core-{build,test}.log`, and `parent-full-core.trx`. No live service was accessed. H09 is locally implemented, reviewed and fully validated offline; main integration, hosted PR review/CI and merge remain.

## Integrated H10a validation

The pushed H10a head `df63c09b1d60f43feed3366820dd7afbae143830` merged locally as `ede0b438f5b18b6931ca5207a1f8e657331dcade`. The only conflict was the readiness tracker: resolution retained the complete reviewed H09 row and complete integrated H10a row. `PostBuilderViewModel` merged automatically and retains both H09 live-write lease ownership and H10a read-generation/cancellation guards; no source conflict required manual redesign.

Sequential `CI=true` Release gates passed with zero warnings/errors: Core built and passed 598/598, then the Windows App built with `EnableWebView2=true` and passed 1,630/1,630, all with zero failures/skips. Evidence is `artifacts/h09/integrated-h10a/core-{build,test}.log`, `core-test.trx`, `app-{build,test}.log`, and `app-test.trx`. No live service, browser, sign-in, profile clear or tenant operation was used.

PR #231 merged as main `728df34fd44367edf3db6bc7f781693953bb184c`, whose tree is identical to the already validated H10a head. Merging that main commit for ancestry produced local merge `9d8240ea9ff3cbc9b4d8b4bcd58290f596872dc0`; source and test trees are unchanged from validated `ede0b438f5b18b6931ca5207a1f8e657331dcade`, so the 598/598 Core and 1,630/1,630 App gates remain applicable without rerun. H10a post-merge CI `36210765580` passed all four jobs, including package-smoke. H09 is current-main integrated and ready for hosted review/CI.

## PR #232 Linux presence-test synchronization correction

PR CI run `36211218704`, Linux job `108317980971`, built with zero warnings/errors and failed only `AsyncProfileUiTests.Committed_secret_presence_wins_over_late_target_read_without_overwriting_other_targets`: the test released all three fake presence reads but used one `Task.Yield` rather than awaiting the actual refresh command, so Linux observed Dataverse presence still `Loading`. Windows App 1,630/1,630 and Core 598/598 passed in the same run.

This is a test-only synchronization correction. The affected test now captures and awaits the actual `RefreshSecretPresenceCommand.ExecutionTask` after releasing its fake gates. The two other new overlapping-presence cases were audited and likewise capture/await the older operation before asserting its late result cannot publish. Assertions and production presence ownership are unchanged; no sleep or weakened condition was introduced. Local focused `AsyncProfileUiTests` passed 24/24, the `CI=true` Release App build with WebView2 passed with zero warnings/errors, and the full App suite passed 1,630/1,630. Evidence is `artifacts/h09/pr232-review/async-profile-ui.{log,trx}`, `app-build.log`, `app-test.log`, and `app-test.trx`. Core source is unchanged and was not rerun. Linux CI rerun remains pending; this document does not claim it passed.

## PR #232 review-fix pre-mortem

1. **Synchronous cache cleanup stalls Save after the database commit.** Release the profile/live-write gate and editor owner first, then offload best-effort old-identity eviction.
2. **A late or faulted cleanup damages current UI state.** Capture the immutable old profile, auth dependency and lifetime token; observe every fault and never publish status/UI from cleanup.
3. **Persistence cancellation exists but is unreachable.** Bind one visible footer Cancel button to the currently accepted persistence CTS, outside the disabled editor.
4. **A late Cancel reports rollback after the database already committed.** Cancellation only requests stop; it never releases ownership early or adds a post-commit token check, so the facade's committed truth remains authoritative.
5. **A cancellation-ignoring credential preflight dispatches a stale probe.** After preflight awaits, recheck caller cancellation and the captured snapshot/generation before refusal/status publication or tester/auth dispatch.

## Historical PR #232 review correction: superseded eviction approach

Greptile reviewed exact head `994a36418cc7fbe3a66e26f0faa5e716b6c2be76` and opened P2 findings `4109872162` (thread `PRRT_kwDOQmkX-M6mM-GF`) for awaited session eviction prolonging Save and `4109872165` (thread `PRRT_kwDOQmkX-M6mM-GH`) for unreachable persistence cancellation. Parent source review also confirmed that `RunProbeAsync` lacked a cancellation/current-generation guard after its new async credential preflight.

Behavioral RED failed 6/6: two synchronously blocking eviction cases timed out, three cancellation-ignoring preflight drift cases dispatched the stale tester, and the attached footer had no Cancel control. Save now completes coherent UI publication, releases its profile/live-write lease and persistence owner, then schedules captured old-identity eviction on `Task.Run` with the captured auth dependency and lifetime token. Cleanup catches every auth fault and guards best-effort trace output too, so a failed trace listener cannot fault the discarded task; cleanup never publishes UI/status. One footer `Cancel update` command targets only the accepted persistence CTS; it does not release ownership early or add a post-commit cancellation check. Probe preflight now rechecks caller cancellation and the captured snapshot/generation before refusal/status publication or tester dispatch.

Parent reviewed and accepted the bounded production/UI/test changes. The focused review suite passed 34/34, including blocking/faulting eviction, explicit SignOut compatibility, attached pre-commit Cancel, post-commit truth, non-queue/write exclusion, and selection/draft/A-B-A stale-preflight suppression. The final `CI=true` Release App build with WebView2 passed with zero warnings/errors and the full App suite passed 1,638/1,638 with zero failures/skips. Evidence is `artifacts/h09/pr232-review/review-red-2.{log,trx}`, `review-green-2.{log,trx}`, `review-app-build-final.log`, `review-app-test-final.log`, and `review-app-test-final.trx`. Core source is unchanged; the prior 598/598 Core gate remains applicable. No Linux rerun is claimed.

## Cached sign-in contract pre-mortem and premises

1. **A retained cache is used for the wrong newly saved context.** Token acquisition keys and requests use the saved client/tenant/resource snapshot, while existing principal-snapshot mismatch guards reject incompatible client-secret principals.
2. **Saving one profile signs out unrelated profiles sharing a client/tenant cache key.** Save performs zero implicit SignOut calls; cached delegated sign-ins are cleared only by the explicit Sign out command.
3. **Shutdown leaves partially completed Save side effects.** Save queues no authentication cleanup; atomic profile/vault persistence remains the complete mutation boundary.
4. **Explicit logout is lost while removing implicit eviction.** Retain the Sign out command and its tested client/tenant-wide status and tooltip.
5. **Vault-secret reference cleanup is accidentally removed with cache cleanup.** Do not change Core profile/auth/vault source; retain atomic secret-rotation and reference-cleanup coverage.

This is the bounded product default after the optional preference window elapsed without a reply; it is a parent design judgment under the authorized readiness fixes, not a claim that the user selected an option.

## Cached sign-in contract resolution

Save now persists profile/settings/credential-reference state only and makes zero implicit `SignOutAsync` calls for mode-only, client, tenant or other profile edits. The next `CoreAuthService` acquisition constructs its request from the newly saved immutable client/tenant/resource context; existing `ValidatePrincipalSnapshot` coverage rejects a mismatched client-secret principal. Automatic protected-secret rotation and unreferenced-vault cleanup remain inside the atomic store and are unrelated to delegated token-cache lifetime.

The explicit Sign out command remains the sole UI action that clears cached delegated sessions. Its client+tenant cache key can be shared by multiple profiles, so the existing command status and tooltip continue to state the real tenant-wide scope. Saving immediately before disposal schedules no authentication work and therefore leaves no queued cleanup to skip or race with a newly acquired shared session.

The prior PR232 review-fix section describing deferred old-identity eviction is retained as historical evidence of the superseded approach. Greptile follow-up findings `4109946988` (thread `PRRT_kwDOQmkX-M6mNJl6`) and `4109946993` (thread `PRRT_kwDOQmkX-M6mNJl-`) showed that deferred deletion could erase a newer same-key session or be skipped during shutdown. The final contract removes that side effect instead of adding queues, tombstones, shutdown waits, persistent MSAL applications or cache-key redesign. This is still the recommended parent design judgment after no product-preference reply, not a claim of explicit user selection.

Cached-sign-in contract RED restored the superseded conditional Save eviction: mode-only, client, tenant and immediate-disposal cases failed 4/4 while the pure rename control passed. With implicit eviction removed, the bounded cached-sign-in/explicit-signout/Core-auth/atomic-profile/cancellation suite passed 133/133. A final local adapter control saves a changed interactive client, tenant and resource through `ProfilesViewModel`, then proves the real injected `CoreAuthService` request uses that saved identity and normalized resource; it and the zero-implicit-signout controls passed 5/5. The final `CI=true` Release App build with WebView2 passed with zero warnings/errors, and the full App suite passed 1,640/1,640 with zero failures/skips. Core source is unchanged by diff, so the prior 598/598 Core gate remains applicable. Evidence is `artifacts/h09/pr232-cache-contract/cache-contract-red.{log,trx}`, `focused-green.{log,trx}`, `cache-contract-adapter.{log,trx}`, `app-build-final.log`, `app-test-final.log`, and `app-test-final.trx`. Fake handlers and temporary stores only; no MSAL sign-in/cache file, user profile database or live service was accessed.
