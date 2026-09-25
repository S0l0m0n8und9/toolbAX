# Atomic Profile Persistence — Phase 1 Core Design

**Status:** Core transaction and App service-facade phases implemented and locally validated. H09 remains incomplete until the UI save/delete/secret flows use the asynchronous facade.

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
