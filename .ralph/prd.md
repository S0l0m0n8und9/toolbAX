# toolbAX — Product Requirements Document

## Overview

toolbAX is a Windows desktop application for Microsoft Dynamics 365 Finance & Operations (F&O) administrators and developers, inspired by XrmToolBox. It provides a plugin-based host that surfaces operational tools — OData query building, dual-write map browsing, entity inspection, and automated integration validation — against live F&O and Dataverse environments. The application targets .NET 8 Desktop Runtime on Windows 10/11, authenticates via MSAL (Entra ID / service principal), and stores environment profiles locally using SQLite with DPAPI-encrypted secrets.

## Goals

- Deliver a reliable, self-updating tool that reduces manual effort for F&O data engineers and integration specialists.
- Complete the **Testify** validation framework so teams can automatically verify dual-write map sync correctness with full enum coverage and idempotent reruns.
- Reach a shippable packaging state: signed MSI/bundle with locked GUIDs and a working update channel.

## Testify Configuration UI

Currently, Testify configuration requires direct JSON editing of `testify-configurations.json`. A dedicated WPF settings panel inside the DualWriteMapBrowser plugin should allow users to view and edit per-map settings (omit fields, preferred values, CE poll timeout, partial enum coverage toggle) without leaving the tool. The panel must read from and write to `TestifyConfigurationStore` and reflect saved state immediately on load.

## Testify Rollback & Idempotent Cleanup

When a Testify run creates an F&O test record but fails during PATCH or CE-verification phases, the created record is orphaned until the next manual cleanup run. Automatic rollback on failure should delete the created record and clear the cached `lastEntityInstanceUrl` so the next run starts clean. The cleanup phase should also gracefully handle records already deleted externally without surfacing errors.

## Testify Enum Coverage Reporting

Partial enum coverage currently produces a low-visibility warning only when `allowPartialEnumCoverage=true`; unmapped enum members cause silent blocking. Coverage gaps should be surfaced prominently in the Prepare phase — listing each unmapped enum member by field and value — and the result grid should distinguish "Blocked: incomplete coverage" from "Blocked: missing entity" with actionable detail. The `TestifyPlanner` output and `TestifyResultRow` model must carry per-field gap detail.

## Testify Test Coverage

Integration and contract tests for the Testify pipeline are incomplete: idempotent rerun with a stale cached URL, payload trimming edge cases, CE poll timeout expiry, and automatic rollback on mid-run failure all lack test coverage. These scenarios should be covered in `DualWriteMapBrowserTestifyTests.cs` using the existing `FakeODataServer` harness. All new tests must pass under `dotnet test` with no parallelism issues.

## Installer & Packaging Finalization

The WiX/Burn installer has placeholder GUIDs, no code-signing step, and a missing .NET Desktop Runtime 8.0 installer path. ProductCode, UpgradeCode, and Bundle GUID must be locked; the build script (`install/build.ps1`) must integrate `signtool` for MSI/bundle signing; and the Burn bundle must chain the correct .NET Desktop Runtime installer. Until these are completed, the published artifact cannot be distributed via the update channel.

## Auth & Token Resilience

Token acquisition can silently fail on `invalid_grant` without a user-facing recovery path. The auth service should retry with an interactive fallback (device code or browser prompt) when silent token acquisition fails, and the host UI should surface a clear re-authentication prompt instead of propagating HTTP 401 errors to plugin operations. Per-tenant authority resolution should be validated against the environment's `TenantId` to prevent cross-tenant token misroutes.