# Compare result context (H06c proposal)

## Status and verified baseline

This is a specification only; no source, tests, UI, or tracker state changes are proposed here.

`DualWriteCompareViewModel` snapshots source/target for service invocation but commits by lifecycle generation only. Selector changes do not invalidate prior/in-flight results. `RefreshEnvironments` reloads/rebinds immutable profiles by ID on activation but explicitly leaves results untouched, and Shell caches the VM. Core comparison already retains H02 sequential UI-context sign-in and H06b Unknown/scope behavior; preserve both.

## Result binding

Each accepted compare captures immutable source/target profiles, their `EnvironmentIdentity` values, and a monotonic selection/result generation. On successful completion publish one immutable result-context record containing captured source/target names, URLs, and completion time. Results render with that context; mutable current picks never relabel old rows.

Any meaningful source/target identity change immediately clears rows, buckets, count, result context, and prior error; increments generation; and requests cancellation of the accepted operation. A→B→A remains invalidated. Do not auto-rerun.

## Refresh and entry rules

`RefreshEnvironments` batches clear/repopulate/rebind so transient ComboBox nulls do not clear unchanged results. Compare identities before/after the batch. Same-ID connection/auth/company edits or removals invalidate; cosmetic changes may retain rows with their original captured attribution.

At entry and after await, validate captured identities against current store snapshots by exact profile ID plus generation/current selection. Store drift before entry refreshes choices and returns a check-selections/run-again message without fallback execution. Completion drift discards rows and refreshes choices; it never publishes under edited profiles.

## Ownership and lifecycle

At most one compare is accepted, including direct `ExecuteAsync` calls. A private linked cancellation source belongs only to the accepted operation; rejected callers cannot replace it. Selection/dispose cancellation does not release busy ownership until the owner finishes. A cancellation-ignoring service may finish, but stale result/error is discarded. Only the owner releases busy/lease state; dispose releases resources without late publication.

Keep matching/verdict/scope behavior and same-host eligibility. Do not change gateway/auth/paging/parser behavior, add an event bus, queue deferred compares, or introduce writes.

## Pre-mortem

1. Late A/B rows appear under C/B picks: captured scope, immediate invalidation, and generation checks.
2. A→B→A admits stale work: monotonic generation.
3. Same-ID edit/removal retains old rows: batched refresh and store identity checks.
4. Cancellation/rejected overlap loses ownership: private accepted-operation handle and owner-only release.
5. Labels mislead or ordinary activation destroys valid rows: immutable attribution and unchanged-refresh proof.

## Acceptance before implementation completion

Use gated-service tests for clearing and late result/error rejection after source/target edits, A/B/A, same-ID store edits, deletion, disposal, cancellation, and rejected overlap. Prove unchanged refresh preserves results, invalid entry makes zero service calls and never falls back, and attached headless selectors/result attribution show the captured pair. No sleeps or live calls.
