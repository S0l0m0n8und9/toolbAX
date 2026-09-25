# Debug-write confirmation

Status: Accepted for implementation. Campaign H13a, 2026-09-25.

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
| The scope message overstates or understates affected projects. | Duplicate project IDs count once; list actual selected map/project labels and disclose skipped missing IDs; assert the request before releasing the dialog. |

Tests use fake connectors, metadata, OData and dialogs only. The preserved partial source lives under
ignored `artifacts/h13a`; baseline `684e6bc` must fail the new confirmation behavior tests before the
completed implementation is tested. Root runs complete solution gates after the source checkpoint.
