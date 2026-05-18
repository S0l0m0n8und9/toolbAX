# toolbAX

## Overview

toolbAX is a Windows desktop application targeting Microsoft Dynamics 365 Finance & Operations administrators and developers. It provides a plugin-based host surface for operational tools — OData query building, dual-write map browsing, entity inspection, and automated integration validation — against live F&O and Dataverse environments.

The application runs on .NET 8 Desktop Runtime (Windows 10/11), authenticates via MSAL (Entra ID or service principal), and stores environment profiles locally in SQLite with DPAPI-encrypted secrets. The plugin architecture mirrors XrmToolBox: each capability ships as an independently loadable plugin within a shared host shell.

## Goals

- Reduce manual effort for F&O data engineers and integration specialists through a reliable, self-updating desktop tool.
- Complete the Testify validation framework so teams can automatically verify dual-write map sync correctness with full enum coverage and idempotent reruns.
- Reach a shippable packaging state: signed MSI/Burn bundle with locked GUIDs and a functioning update channel.
- Harden authentication so silent token failures never silently propagate as HTTP 401 errors to plugin operations.

## Scope

### Testify Configuration UI

A WPF settings panel embedded in the DualWriteMapBrowser plugin that reads from and writes to `TestifyConfigurationStore`. The panel must expose per-map settings: omit fields list, preferred values map, CE poll timeout (seconds), and partial enum coverage toggle. State must be reflected immediately on panel load with no separate reload step. Users must be able to save changes and see confirmation without leaving the plugin.

### Testify Rollback and Idempotent Cleanup

When a Testify run creates an F&O test record and then fails during the PATCH phase or CE-verification phase, the run must automatically delete the created record before surfacing the failure. The cached `lastEntityInstanceUrl` must be cleared on rollback so subsequent runs start from a clean state. The cleanup logic must handle records that were already deleted externally (e.g., 404 response) without raising an error.

### Testify Enum Coverage Reporting

The Prepare phase must enumerate all unmapped enum members for every mapped field and surface them as a structured gap report before any test record is created. Each gap entry must identify the field name and the unmapped enum value. `TestifyPlanner` output must carry per-field gap detail. `TestifyResultRow` must distinguish between "Blocked: incomplete coverage" and "Blocked: missing entity" as discrete result states with actionable detail visible in the result grid.

### Testify Test Coverage

Integration and contract tests must be added to `DualWriteMapBrowserTestifyTests.cs` using the existing `FakeODataServer` harness. Required coverage:

- Idempotent rerun when `lastEntityInstanceUrl` is stale or points to a deleted record.
- Payload trimming edge cases (null fields, empty collections, fields in the omit list).
- CE poll timeout expiry producing a deterministic timeout result row.
- Automatic rollback when failure occurs after record creation but before verification completes.

All new tests must pass under `dotnet test` with no parallelism issues.

### Installer and Packaging

The WiX/Burn installer must be finalized for distribution:

- ProductCode, UpgradeCode, and Bundle GUID locked to stable values (generated once, committed to source).
- `install/build.ps1` must invoke `signtool` to sign both the MSI and the Burn bundle; the script must fail the build if signing fails.
- The Burn bootstrapper must chain the correct .NET 8 Desktop Runtime redistributable with a valid installer path and version condition.
- The resulting artifact must be accepted by the update channel endpoint without modification.

### Auth and Token Resilience

When silent token acquisition fails with `invalid_grant` or equivalent, the auth service must attempt an interactive fallback (device code flow if no interactive session is available, browser prompt otherwise) before returning a failure. The host shell must surface a re-authentication dialog at that point rather than allowing HTTP 401 responses to reach plugin operations. Per-tenant authority resolution must validate the resolved authority against the environment's `TenantId` and reject mismatches with a clear error before any API call is made.

## Non-Goals

- No macOS or Linux support.
- No web-based or browser-hosted version of the tool.
- No support for Dynamics 365 CE/CRM-only environments (must have F&O with dual-write enabled).
- No cloud-hosted backend; all state remains local to the user's machine.
- No plugin marketplace or auto-discovery of third-party plugins in this release.
- No multi-user or team-shared configuration; profiles and secrets are per-machine.
- No CI/CD pipeline changes beyond what is required by the packaging build script.

## Success Criteria

- The Testify configuration panel reads and writes all supported settings fields without data loss across save/reload cycles.
- A Testify run that fails after record creation leaves no orphaned records in F&O and resets cached URL state; the subsequent run completes without manual cleanup.
- The Prepare phase reports all unmapped enum members by field and value before attempting record creation; the result grid shows distinct blocked states for coverage gaps versus missing entities.
- All new tests in `DualWriteMapBrowserTestifyTests.cs` pass under `dotnet test` with the full suite; no flaky failures under repeated runs.
- The installer build script produces a signed MSI and bundle; `signtool verify` succeeds on both artifacts.
- Installing the MSI on a clean Windows 10/11 machine with no prior .NET runtime bootstraps the correct .NET 8 Desktop Runtime and launches the application.
- Silent token failure triggers interactive re-authentication within one retry; no HTTP 401 error surfaces in plugin result output after the user completes re-auth.
- Cross-tenant token misroutes are caught at authority validation time and produce a user-readable error before any API call is dispatched.

## Work Area: Testify Configuration UI

The settings panel is a WPF UserControl hosted in the DualWriteMapBrowser plugin. It binds directly to `TestifyConfigurationStore` via a ViewModel that calls load on construction and save on an explicit user action. The control must handle the case where no configuration exists for a given map (show defaults, save on first write). Validation must prevent saving a CE poll timeout below 5 seconds or above 300 seconds. The panel must be accessible from the plugin's main toolbar without navigating away from the map list.

Implementation sequence: define ViewModel contract, implement load/save wiring against `TestifyConfigurationStore`, build XAML layout, wire validation, add integration test asserting round-trip fidelity.

## Work Area: Rollback and Idempotent Cleanup

The rollback logic lives in the Testify runner, triggered on any exception or non-success result after the record creation step. It must issue a DELETE to the F&O OData endpoint for the created entity instance URL, treat 404 as success, and re-throw the original failure after cleanup completes. The `lastEntityInstanceUrl` cache key must be cleared regardless of whether the DELETE succeeded. The idempotent rerun path must re-validate the cached URL with a HEAD or GET before using it and invalidate it on 404.

Implementation sequence: add rollback method to runner, integrate into the failure path, add stale-URL validation to the run entry point, write tests for each scenario.

## Work Area: Enum Coverage Reporting

`TestifyPlanner` must collect unmapped enum members during the field-mapping analysis phase and attach them to a new `EnumCoverageGap` collection on its output. `TestifyResultRow` must add two new blocked states with a structured detail payload. The result grid renderer must display gap detail inline (field name, unmapped values as a comma-separated list) for coverage-blocked rows. The warning currently emitted when `allowPartialEnumCoverage=true` is retained but supplemented with the gap detail.

Implementation sequence: extend `TestifyPlanner` output model, update planner analysis logic, update `TestifyResultRow` model, update grid renderer, write tests asserting gap detail appears in planner output and result rows.

## Work Area: Test Coverage Gaps

All four missing scenarios (stale URL rerun, payload trimming, poll timeout expiry, mid-run rollback) must use `FakeODataServer` to simulate F&O responses. Tests must be deterministic: poll timeout tests must use an injected clock or configurable timeout value rather than real wall time. Each test must assert both the result row state and any side-effect state (cache cleared, record deleted, etc.). Tests must be added to the existing `DualWriteMapBrowserTestifyTests.cs` file and must not require additional test infrastructure beyond what already exists.

## Work Area: Installer and Packaging

All three GUIDs (ProductCode, UpgradeCode, Bundle) must be generated and committed before any other packaging change is made to prevent future upgrade-path breaks. The `install/build.ps1` script must accept a certificate thumbprint parameter, locate `signtool.exe` via the Windows SDK path, sign the MSI, then sign the bundle. The Burn chain must reference the .NET 8 Desktop Runtime offline installer by a resolved local path or a verified download URL with a hash check. The build script must emit a summary of signed artifacts and their thumbprints to stdout.

Implementation sequence: lock GUIDs, add signtool integration to build script, resolve and chain .NET runtime installer, end-to-end test install on a clean VM image.

## Work Area: Auth and Token Resilience

The auth service must catch `MsalUiRequiredException` and `MsalServiceException` with error code `invalid_grant` and initiate the interactive fallback before returning. The fallback strategy must be configurable per environment profile (device code preferred, browser preferred, or prompt user to choose). The host shell must subscribe to an auth-required event raised by the service and display a modal re-authentication dialog that blocks plugin operations until re-auth completes or the user cancels. The per-tenant validation must compare the authority hostname derived from the acquired token's `tid` claim against the configured `TenantId` and surface a mismatch as a named error type.

Implementation sequence: add fallback logic to auth service, wire auth-required event to host shell dialog, implement tenant validation, write unit tests for each failure mode and fallback path.