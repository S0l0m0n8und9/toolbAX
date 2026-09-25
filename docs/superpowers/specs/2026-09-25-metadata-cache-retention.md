# Metadata cache retention

Status: Accepted for implementation. PR 218 P2; parent-approved scope, 2026-09-25.

After successful housekeeping, retain three unleased `catalog-meta-v1` metadata partition groups per
exact ordinal profile ID, plus every partition leased by an in-flight operation in this process.
Rank by the newest fetched/validated `UpdatedUtc`, with an ordinal key tie-break. This is not LRU;
retention never touches timestamps or extends freshness. Failed operations' leftover rows become
eligible when their leases are released.

Parse the entire three-string JSON key `[profileId, baseUrl, partition]`. Delete only known metadata
kinds (`ODataMetadata`, `ODataMetadataXml`, `ODataEntityIndex`, `ODataEntityDetails:*`), filtering both
candidate selection and deletion. Delete chosen groups atomically using parameterized SQL. Other
profiles, malformed/future keys, unknown kinds, legacy namespaces, Tables and imports are untouched.
No schema registry or profile/authentication/UI change is needed.

Share lease/prune coordination by canonical file database path within this process. Acquire the lease
before waiting for the existing per-partition load semaphore; release it on success, cancellation and
failure. Admission, counter updates and SQLite pruning use short critical sections; authentication and
HTTP never hold the retention gate. Cleanup is bounded, best effort, never replaces the result or original
error, and cannot leak a lease. Diagnostics contain only safe error-type summaries. Coordination objects
are never disposed while owners may release. A shared prune generation invalidates warmed XML memos
across service/store instances in this process.

This is a count bound, not a fixed byte limit. Deleted SQLite pages are reusable, but the database file
does not necessarily shrink. Cleanup failure may leave temporary excess. No global cross-process lease
or strict cross-process retention guarantee is made; existing external-change snapshot semantics remain.

## Pre-mortem and deterministic proof

| Failure mechanism | Mitigation and proof |
|---|---|
| Prune deletes a partition while a fetch or lock waiter still owns it. | Lease before the per-partition wait; coordinate admission and delete. Hold one fetch and a waiter while four independent partitions complete; protected groups survive and unrelated HTTP progresses. |
| A second service resurrects evicted raw XML from its memo. | Shared prune generation invalidates memos. Warm A, prune through B, revisit A and require a fresh fetch. |
| Prefix matching deletes another profile, future format/kind or imported user data. | Exact complete-key parsing and known-kind filters on selection and deletion. Seed adversarial IDs, malformed/future keys and import sentinels; preserve them. |
| Cancellation/error leaks a lease, or housekeeping failure replaces successful data. | Outer lease-finally and unconditional counter release, bounded independent cleanup, safe failure summary. Test canceled waiter/faulted fetch and a SQLite delete trigger failure while preserving the original result/error. |
| Cleanup invents freshness or removes only some known representations. | Do not touch timestamps; delete the selected group atomically. Exercise same-ETag XML/full/index/details, restart and A-B-A reuse within the limit. |

Validation is local only: real temporary SQLite stores, fake HTTP handlers, controlled task gates,
bounded watchdogs where necessary, and no sleeps or live tenant calls. Parent runs full suites after
the P1/P2 source freeze; implementation owns focused RED/GREEN evidence.
