# Profile connection test snapshots (H08a)

## Accepted scope

Test the displayed draft, without saving or activating it. H07 already removed the former draft-persistence path; current tests do not save profiles. Zero persistence is an invariant to prove, not a remaining bug claim. No auth-cache/signout changes, package additions, live calls, remote actions or unrelated hardening. D03 URL prefix handling and H09 aggregate persistence remain separate.

Build one immutable EnvProfile draft at probe entry using the same field/default/null handling as Save through a narrow shared pure builder. Capture profile ID, full connection identity and displayed name/endpoint. FO and Dataverse have independent visible statuses; the gateway keeps DiStatus. Test completion never overwrites global save/activation Status.

FO/Dataverse tests refuse unsupported target modes and nonempty pending target-secret input before any probe/auth call, with explicit Store guidance. ClientSecret also requires the saved mode/effective client ID/tenant to match the draft and an available stored credential; changed auth requires Save then Store before Test. URL/name-only drafts can use that stored credential. Interactive tests need no stored principal. Portal gateway testing retains H07's interactive portal policy and never consumes FO/Dataverse typed secret input or legacy DI settings as a new credential.

Draft edits, selection/null/removal and saves invalidate all three results through one monotonic generation, cancel pending work and discard late success/errors, including A-B-A. Each target has a separate accepted owner/private cancellation source and non-queuing admission before token allocation; targets may run concurrently without stealing each other's status or cancellation. No reactive framework or pending-secret API.

Profiles disposal invalidates/cancels all probes. Parent approved the missing production lifecycle hookup: idempotent Shell IDisposable disposes only already-cached Profiles and IDisposable tools, without resolving new content; MainWindow.Closed invokes it. CoreConnectionTester preserves probe endpoints/forceRefresh/normalization, rethrows caller cancellation and checks it before auth, after auth/before HTTP, and after response. Timeout without caller cancellation remains a failed probe. A passing endpoint probe is not a guarantee that every tool/capability will work.

## Five pre-mortems

1. Visible drafts are ignored: assert exact captured draft arguments for all three probes.
2. Test accidentally saves or activates: recording profile/secret stores assert zero writes and unchanged active ID/credentials.
3. Selection/null/same-ID edit/A-B-A misattributes a late result: generation plus captured identity/name and profile membership guards.
4. Concurrent targets or reruns steal status/Cancel: independent accepted-owner commands, per-target results and cancellation controls.
5. Cancellation or close continues HTTP: Core token checks and real attached-window disposal tests, including cancellation-ignoring fakes.

## Verification

Use gated local fakes and isolated stores, no sleeps/live services. Capture completed behavioral RED/GREEN for draft arguments, no persistence on success/failure/cancel, secret-policy refusal/controls, drift/null/delete/save/A-B-A, completed-result invalidation, same-target overlap, cross-target independence, cancellation/disposal, Core cancelled-auth zero HTTP and cancelled late-response no success, and attached attribution/cancel/close behavior. Save focused CI=true Release logs/TRX under artifacts/h08a. Parent reviews source and runs full gates after source freeze.

## Source-ready verification

Completed behavioral baseline RED: `draft-red.trx` failed 5/5 for ignored draft fields and pending-secret probes. `probe-cancellation-red.trx` failed 4/5 caller-cancellation cases while its genuine-timeout control passed. Parent/local pre-review then identified credential presence lookup outside the guarded path; `credential-lookup-red.trx` failed both FO/DV throwing-store cases. That lookup now reports an attributed per-target failure with zero probes/writes. Shared URL normalization uses Uri.TryCreate fallback and null-safe identifier handling; explicit malformed draft controls for all three probe kinds complete without a current-scope validation fault.

Final CI=true Release focused validation passed 238/238, zero failed/skipped (`artifacts/h08a/source-ready-green.log` and `.trx`). Coverage includes exact full draft arguments/modes and Save-equivalent null/default handling; recording-store invariants on success/failure/cancel; stored-credential policy; 54 gated changed-context success/error cases across all three targets; completed-result invalidation; same-target ownership and concurrent-target independence; disposal; cancelled auth/late HTTP; real attached MainWindow close; and visible per-target attribution/Cancel controls. Existing H07 and Shell VM/render controls passed in the same run. No live services, full-suite run or remote action was performed.

`ProfilesViewModel` now implements IDisposable. The approved narrow missing-host hookup adds idempotent Shell disposal of only existing cached Profiles/IDisposable tools and invokes it from MainWindow.Closed; it never resolves/recreates a tool or changes navigation/cache policy. Save still performs its original persistence and auth-cache eviction only when explicitly invoked. The shared pure draft builder does not add persistence to Test. Parent review/integration/full gates remain before PR delivery.

## Cancelled-probe fault observation

Parent review identified a .NET 10 lifecycle edge: cancellation of WaitAsync removes its continuation from the original unfinished task ([runtime Task.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs)). A cancellation-ignoring probe could then fault after its accepted command completed and reach TaskScheduler.UnobservedTaskException despite the UI generation guards.

The private Profiles-only wait helper now attaches a static, fault-only exception observer to that original FO/Dataverse or gateway task when its wait is cancelled. It uses no cancelled scheduling token, retains no VM closure, publishes/logs nothing and does not await the late operation. Prompt cancellation, accepted ownership, auth, persistence and result guards are unchanged.

Completed RED `artifacts/h08a/late-fault-red.trx` failed 3/3: each FO, Dataverse and gateway canary raised an unobserved-fault event after command cancellation. Tests use unique canaries, bounded collection/finalizer cycles, weak-reference collection checks and a positive unobserved control; no reflection into private task implementation. Each also proves the command completes before the original probe settles and its late fault cannot change status. Final CI=true Release focused suite passed 241/241, zero failed/skipped (`artifacts/h08a/late-fault-green.log` and `.trx`), including all prior draft, credential, cancellation, generation, render and Shell controls. No full-suite, live-service or remote validation was performed for this correction.

## Integrated local validation

Parent accepted the snapshot/status/ownership implementation, preflight credential-store failure controls and cancelled-probe late-fault observer. The finalizer-canary test class now belongs to its own xUnit collection with DisableParallelization=true, avoiding interference with unrelated process-wide exception-handler tests without changing global parallelism. The earlier focused 241/241 proof and all recorded RED evidence remain retained above.

At integrated source `eb170cbe8d65ece184312b1bcdae8a2eb110c229`, the parent restored unchanged dependencies in the worktree and both CI=true Release solutions built with zero warnings/errors. Full Windows App validation with EnableWebView2=true passed 1,470/1,470 and Core passed 456/456, zero failed/skipped. Evidence is `artifacts/h08a/full-{app,core}-{restore,build,test}.log` and `full-{app,core}.trx`. No live services were used. H08a is locally validated and awaits its PR, hosted CI/review and merge; D03 URL prefix handling and H09 aggregate persistence remain separate.

## PR #229 review correction: legacy clear confirmation ownership

Greptile finding `4109421359` against reviewed head `c112ee63584386a148086c4d4de9c237b83cad6e` identified that clearing a legacy Data Integrator password wrote its confirmation into `DiStatus`. Any subsequent draft edit invalidated gateway probe output by clearing that same property, so the unrelated successful clear confirmation disappeared. `LegacyDiStatus` now exclusively owns the clear confirmation and is reset only when profile selection changes; `DiStatus` remains exclusively gateway-probe owned. The dedicated confirmation control sits outside the legacy-warning border so a password-only legacy profile can hide its warning and Clear button after removal while keeping the successful confirmation visible.

A labelled behavioral RED temporarily restored draft invalidation of `LegacyDiStatus`; 4/168 focused tests failed, including attached retained-configuration and password-only render controls. After removing that mutation, the regression slice passed 168/168. The CI=true Release App solution then built with WebView2 enabled and zero warnings/errors, and the final Profiles/H07/H08/render/Shell subset passed 305/305 with zero failures/skips. Evidence is under `artifacts/h08a/pr229-review/`, including `pr229-review-behavioral-red.{log,trx}`, `pr229-review-regression-green.{log,trx}`, `pr229-review-app-build.log`, and `pr229-review-focused-final.{log,trx}`. No live services or full-suite claim is included; parent review and the full App gate remain outside this correction's local validation.
