# Compare evidence and scope

Status: Implemented and focused validation passed; full gates, PR/review and merge pending. Campaign H06b, 2026-09-25.

Append `DualWriteComparisonVerdict.Unknown` without changing existing enum values. Pairing remains
unchanged: ambiguous identities and one-sided presence keep their existing verdicts. For a confidently
paired map, any null, empty or whitespace active version/state on either side produces Unknown before
comparing reported values. Keep displayed values and add a concise Note naming exactly each missing
source/target field. A missing ActiveTemplate counts as missing active version. Nonblank unfamiliar
versions/states remain reported values; do not add schema validation or change parsing/normalization.
Complete evidence retains existing version-first/state-second and case-insensitive comparison semantics.

`IsDifference` remains the compatibility non-identical attention flag, including Ambiguous and Unknown;
it is not proof of configuration drift. App summaries bucket Unknown separately, label it `unknown`,
use WarnBrush and expose its reason in the existing note marker/tooltip. The Identical enum's friendly
label becomes `reported values match`. Result scale says `1 map row` / `N map rows`, since uncertain
and unpaired rows are included. The fake seed includes a reason-bearing Unknown row.

The view states that this read-only comparison covers reported map presence, active version and state.
Field mappings, filters, integration keys and other configuration are not compared. Replace the
unverified shared-credential caption with read-only scope wording, using existing styles and wrapping.
No compare request, identity pairing, auth, retry or deeper configuration-comparison change is allowed.
Compare selection/result attribution remains H06c and malformed response parsing remains H06a.

## Pre-mortem and deterministic proof

| Failure mechanism | Prevention and proof |
|---|---|
| Paired blanks look identical. | Explicit source/target and version/state missing-evidence detection; test null/empty/whitespace matrix and absent ActiveTemplate. |
| Missing evidence is presented as a mismatch. | Unknown precedes value comparison; retain known/blank displayed values. Test missing state alongside differing versions and missing version alongside differing states. |
| One-sided or ambiguous maps lose their existing meaning. | Preserve identity/presence precedence; regression cases combine missing metadata with one-sided and ambiguous identities, including degraded target pairing. |
| UI or fake chips hide Unknown or color it green. | Distinct bucket, friendly label, warning brush and precise reason; assert a realized attached DataGrid row and marker tooltip, plus fake all-verdict coverage. |
| A match is mistaken for full configuration parity. | Use `reported values match`, map-row count and visible limited scope. Verify these together with an actual Unknown row instead of redundant copy-only tests. |

Use fake data and local Core/headless tests only, with meaningful RED/GREEN and no sleeps or live calls.
Focused `CI=true` Release Core comparer and App compare/fake/render results go under ignored
`artifacts/h06b`. Parent controls full solution gates after source freeze and base integration.

Focused evidence: clean baseline passed 18 Core comparer and 36 App compare/fake/render tests.
Before production changes, missing-evidence regressions produced 20 Core failures (24 controls passed),
and new presentation expectations produced 10 App failures (30 controls passed). Completed `CI=true`
Release focused runs pass 47/47 Core and 40/40 App, with zero failed/skipped. The final Core suite adds
explicit matching-null/empty/whitespace controls on both sides. Saved logs/TRX are
`baseline-{core,app}`, `unknown-{core,app}-red`, and `unknown-{core,app}-green` under `artifacts/h06b`.
