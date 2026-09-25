# Debug-write confirmation

Status: PR 221 cancellation review fix implemented and fully validated locally; remote re-review and merge pending. Campaign H13a, 2026-09-25.

Enable and disable both change project-level F&O debug flags and must confirm before metadata or HTTP work. The operation retains its H01 owner-held mutation lease, captures project IDs/labels and identity before the dialog, then revalidates cancellation, disposal, and identity before dispatch.

Existing no-session, identity-mismatch, no-selection, no-URL and no-project validation runs before the
dialog. Its request names enable/disable, the captured F&O name/URL, the unique project count and
the selected affected map/project lines. It explains the project-level flag/logging scope and discloses
the number of selected maps lacking a project ID. Duplicate project IDs cause only one project write.

Decline, dialog fault and cancellation before dispatch report that no changes were sent. Disposal
returns silently without a late status write. The captured session reference and full identity are checked
after the dialog; edits to selection cannot retarget the approved request. The non-queuing atomic lease
remains held before confirmation through completion and is released only by its owner in `finally`.
Existing per-request environment guards remain. Payloads, If-Match, retries, post-dispatch uncertainty,
logging policy and authentication are unchanged.

## Pre-mortem and proof

| Failure mechanism | Prevention and deterministic test |
|---|---|
| A declined enable/disable still reads or writes. | Confirm first; both decline cases assert zero metadata access and zero GET/PATCH calls. |
| Selection changes retarget approval. | Capture immutable map labels and distinct project IDs before the await; mutate selection while the dialog is held and assert exact original GET targets and enable/disable payloads. |
| Approval crosses environment change, cancellation or disposal. | Check cancellation, disposed state, captured session and full identity before metadata/HTTP; gated tests assert zero calls, and every disposed dialog outcome preserves the prior status. |
| A failed or competing command clears the operation owner's busy state. | Preserve the H01 lease/finally; held confirmation rejects direct reconnect, lifecycle and debug calls without extra dialogs; fault/cancel releases the gate for a later confirmed operation. |
| Command cancellation only cancels the caller while the real modal confirmation remains open. | Add a token-aware dialog contract. The desktop service closes the actual `ConfirmWindow` as declined on the UI dispatcher, disposes its registration after completion, and lets cancellation win a racing approval. Headless actual-window tests cover off-thread cancellation, pre-cancel/no-owner fail-closed behavior, normal approval, and prove a late callback cannot close a later dialog. |
| The scope message overstates or understates affected projects. | Duplicate project IDs count once; list actual selected map/project labels and disclose skipped missing IDs; assert the request before releasing the dialog. |

Tests use fake connectors, metadata, OData and dialogs only. All 14 new confirmation cases failed on
baseline `684e6bc`, then the completed implementation passed the focused 115/115 Ops/Shell suite.

Prior full `CI=true` Release validation passed at source `c3f072a9ae2458f8700abc0aa8224720060cb324`:
both solution builds had 0 warnings/errors; App tests passed 1,150/1,150 and Core tests passed 403/403,
with 0 failed/skipped. Evidence is saved under `artifacts/h13a/full-{app,core}-{build,test}.log` and
`artifacts/h13a/full-{app,core}.trx`. No live authentication or environment calls were used.
The cancellation review regression failed because the held fake had to be answered after command cancel.
Fixed locally in `9c14ab1`, the token-aware dialog implementation passes the focused Operations/Shell/DialogService Release suite 122/122,
including actual headless window dismissal, pre-cancel/no-owner behavior, racing approval, registration
lifetime, late fake approval, zero dispatch and lease release. Evidence is under ignored
`artifacts/h13a/cancellation/`. Latest-head full `CI=true` Release gates pass both builds with 0 warnings/errors,
App 1,157/1,157 and Core 403/403, with no failures/skips. Remote re-review, discussion resolution and merge remain pending.
