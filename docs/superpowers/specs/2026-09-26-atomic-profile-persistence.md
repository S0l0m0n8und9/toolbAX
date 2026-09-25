# Atomic Profile Persistence — Phase 1 Core Design

**Status:** Accepted for Core implementation. H09 remains incomplete until the App facade, cache publication and UI save/delete/secret flows use this boundary asynchronously.

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
