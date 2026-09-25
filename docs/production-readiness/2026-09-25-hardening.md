# Production hardening tracker — 2026-09-25

## Campaign record

- Initial reviewed revision: `5933f8108bae62668c91fe5297e9ca52157320fa` (`main` / `origin/main` at review start).
- Objective: address each distinct audited production-readiness item in its own GitHub pull request, resolve any Greptile review findings that occur, and merge the reviewed result before marking the item complete.
- Validation boundary: source, deterministic tests, CI, packaging, and documentation only. No live Dataverse, Finance and Operations, gateway, portal, or tenant sign-ins are authorised for this campaign.
- Completion rule: a passing local test or an open pull request is evidence, but does not complete an item. Completion requires the merged pull request plus evidence that every observed Greptile finding was adjudicated and addressed.
- External prerequisite: production code signing requires a user-provided or approved signing identity or service and its release configuration. No signing identity or certificate will be fabricated or substituted; the current unsigned-release boundary remains explicit until that prerequisite is supplied.

## Validation baseline

| Revision | Solution | Release build (`CI=true`) | Release tests (`CI=true`) |
|---|---|---|---|
| `5933f8108bae62668c91fe5297e9ca52157320fa` | `avalonia/toolBax.slnx` | Passed, 0 warnings and 0 errors | Passed, 1,004/1,004 |
| `5933f8108bae62668c91fe5297e9ca52157320fa` | `FoToolbox.sln` | Passed, 0 warnings and 0 errors | Passed, 366/366 |

Current H02 branch validation: both solutions build in Release with `CI=true`, 0 warnings, and 0 errors; the Avalonia suite passes 1,005/1,005 and the Core suite passes 366/366.

## Scope and evidence

| ID | Scope | Status | Validation evidence | PR, Greptile, and merge evidence |
|---|---|---|---|---|
| H01 | Keep environment identity coherent when switching environments or editing profile URL/auth data; discard in-flight results and invalidate environment-scoped cached tools. | In progress | Source premises verified; design and implementation plan are under review. | Pending |
| H02 | Keep both sequential Compare sign-ins on the caller/UI context; allow map HTTP loads to remain concurrent and context-free; preserve gateway disposal on failure and cancellation. | Merged | Focused Avalonia regression red on the reviewed revision (`[True, False]` UI access), then green after the minimal fix (`[True, True]`). Full post-change Release builds passed with 0 warnings/errors; Avalonia 1,005/1,005 and Core 366/366 tests passed. | [PR #216](https://github.com/S0l0m0n8und9/toolbAX/pull/216), reviewed head `6d9233c902f3ca540f676b3c29ee286f7f8e62b8`, merged as `e7faee6c162f27cc3b05339ee7601f5db7a242ad` at 2026-09-25T02:26:39Z. Head CI `36086041982` and merge CI `36086277463` passed; Greptile rated 5/5 with no findings. |
| H03 | Represent uncertain write outcomes honestly across timeout/cancellation and provide a safe reconciliation path. | Pending | Pending | Pending |
| H04 | Add Windows application CI, a packaged smoke test, and CI-gated release publication. | Pending | Pending | Pending |
| H05 | Enforce gateway/token endpoint trust and validate the appropriate token identity claims for each boundary. | Pending | Pending | Pending |
| H06a | Distinguish malformed dual-write map/virtual metadata from a valid empty or partial-success response. | Pending | Pending | Pending |
| H06b | Return unknown when Compare lacks version/state data and report the honestly compared scope. | Pending | Pending | Pending |
| H06c | Attribute Compare results to their selected environments and invalidate them when selection changes. | Pending | Pending | Pending |
| H07 | Remove unsupported certificate and resource-owner-password auth options from the supported surface; handle legacy profiles explicitly during load/edit so they do not silently imply support. | Pending | Pending | Pending |
| H08a | Keep connection-test drafts separate from persisted profiles and attribute test results to the exact tested snapshot. | Pending | Pending | Pending |
| H08b | Use one shared request URL normalization policy across profile and connection paths. | Pending | Pending | Pending |
| H09 | Make profile/secret persistence atomic and non-blocking, including rollback of `ActiveId` when persistence fails. | Pending | The narrow `CoreProfileStore.ActiveId` persist-before-cache ordering correction is covered by H01. Aggregate atomic/asynchronous profile and secret persistence remains in H09. | Pending |
| H10a | Apply cancellation consistently throughout read operations. | Pending | Pending | Pending |
| H10b | Add bounded retries for safe reads, including throttling-aware delay and cancellation. | Pending | Pending | Pending |
| H10c | Detect paging cycles and report incomplete paging honestly. | Pending | Pending | Pending |
| H11 | Bound export memory, expose truthful progress/cancellation, and verify large-data behaviour. | Pending | Pending | Pending |
| H12 | Mark the profiler CLI experimental; document its current auth, output, scope, deterministic-test, and CI limitations without presenting it as a complete production profiler. | Pending | Pending | Pending |
| H13a | Make debug-write confirmation behaviour consistent across every write entry point. | Pending | Pending | Pending |
| H13b | Make `If-Match` and concurrency behaviour truthful and visible to the caller. | Pending | Pending | Pending |
| H14 | Redact record keys and business identifiers from logs. | Pending | Pending | Pending |
| H15 | Document support/deployment requirements, including WebView2 prerequisites, the actual capability inventory, and the current unsigned-release boundary. Core-only saved-request, template, key, and reset APIs must be described as such unless a separate product decision exposes them in the app. Include Microsoft's explicit warning that the dual-write management API automation reference is provided as-is and may break with API changes. | Pending | Remaining live questions stay in [issue #168](https://github.com/S0l0m0n8und9/toolbAX/issues/168): Resume `skipInitialSync` and `reversedSourceFilter` format. The external compatibility boundary is documented by [Microsoft's Dual-write automations reference](https://github.com/microsoft/Dual-write-automations). | Pending |

## Discovered follow-ups outside the original audit scope

| ID | Follow-up | Status | Source evidence |
|---|---|---|---|
| D01 | Replace POST Builder's hard-coded `USMF` starter payload and field-grid `dataAreaId` default with an environment-aware default in a separately scoped change. H01 still treats `EnvProfile.Legal` as identity so a company edit invalidates affected tool state, but it does not change payload defaults. | Pending | `PostBuilderViewModel` currently seeds `RequestBody` with `"dataAreaId": "USMF"` and `LoadFields` defaults included `dataAreaId` rows to `USMF`, regardless of the active profile's `Legal` value. |
