FO Toolbox – Codex Workplan
============================

Purpose
-------
Actionable breakdown of FO Toolbox into Codex-ready work packages with embedded prompts, references, examples, and test expectations. Keep `toolbAX.md` as the source-of-truth requirements; use this plan to drive discrete builds. Target runtime: **.NET 8.0 (net8.0-windows)** with a future upgrade path to .NET 10 when it RTMs.

Design Critique (gaps to address)
---------------------------------
- Runtime target: .NET “10 LTS” is aspirational; choose current LTS (net8.0-windows) unless .NET 10 is available when building. Document the target and upgrade path.
- Auth scope: F&O requires `https://<env>.operations.dynamics.com/.default` scope and authority per-tenant. Clarify token cache lifetime, retry on `invalid_grant`, and certificate key storage (thumbprint + store location).
- Cross-company semantics: when `CrossCompany=false`, enforce company filters (`dataAreaId`) and surface clear UX about default company vs explicit filter.
- Plugin isolation: define trust model (signed plugins), AppDomain/AssemblyLoadContext boundaries, and resource caps (no network/file access without capabilities).
- Updater: specify channel (stable/beta), rollback strategy, and code-signing chain; avoid silent elevation.
- Telemetry/logging: crash dumps should be non-PII; pick log location and retention; allow user toggle for diagnostics upload.
- Testing surface: add contract tests for `$metadata` parsing, `$expand` limits, CSV streaming back-pressure, and plugin capability enforcement. Provide fake metadata and mock OData server to avoid real env dependency.

How to run Codex tasks
----------------------
Each work package below is self-contained. Feed the “Prompt for Codex” plus the referenced files into Codex. Outputs and tests should land under the suggested paths. Keep everything ASCII unless a file already requires otherwise.

Work Packages
-------------

### WP1 – Host Shell & Plugin SDK
- **Goal:** WPF host shell and minimal plugin SDK with discovery, manifest validation, capability gating, and safe unload.
- **Refs:** toolbAX.md (Architecture, Plugin SDK), this file.
- **Deliverables (paths):** `src/FoToolbox.Host/`, `src/FoToolbox.SDK/`, `src/FoToolbox.Core/`, `plugins/HelloPlugin/`.
- **Prompt for Codex:**
  ```
  Build a WPF host shell (net8.0-windows, MVVM) with a Plugin SDK. Implement plugin discovery from `plugins/` folder, manifest validation (id/version/minSdk/capabilities), and capability-scoped service injection (IODataClient placeholder + ILogger). Use Collectible AssemblyLoadContext for load/unload. Provide a sample HelloPlugin implementing IFoToolPlugin and a manifest JSON embedded resource. Keep public surface minimal per toolbAX.md Plugin SDK contract.
  ```
- **Tasks:**
  - Create solution with projects: Host (WPF), SDK (interfaces + manifest), Core (shared utilities), sample HelloPlugin.
  - Implement manifest loader (JSON), capability enforcement, and safe unload with `AssemblyLoadContext.Unload`.
  - Basic shell UI to list discovered plugins and open their UserControl.
- **Tests (xUnit):**
  - Load valid plugin and instantiate tool.
  - Reject plugin with missing capability or incompatible minSdk.
  - Verify unload frees AssemblyLoadContext (WeakReference collectable).

### WP2 – Auth & Profile Store
- **Goal:** MSAL-based auth with profile CRUD and DPAPI-encrypted secrets persisted in SQLite.
- **Refs:** toolbAX.md (Data & secrets), this file.
- **Deliverables:** `src/FoToolbox.Core/Auth/`, `src/FoToolbox.Core/Profiles/`, `data/profile.db`.
- **Prompt for Codex:**
  ```
  Implement Entra ID auth using MSAL.NET client credentials (with optional interactive). Scope tokens to https://<env>.operations.dynamics.com/.default. Persist environments, service principals, and secrets per toolbAX.md schema using SQLite and DPAPI (CurrentUser). Provide profile CRUD services and a “Test Connection” method that acquires a token and calls GET {baseUrl}/data with HttpClient. Include migration-friendly schema creation.
  ```
- **Tasks:**
  - Define SQLite schema creation/migration code; ensure SecretVault blobs are DPAPI-encrypted JSON or PFX.
  - Profile service: add/update/delete environments and principals; resolve auth parameters per env.
  - Auth service: token acquisition with resilient retry on transient MSAL errors; token cache per profile.
- **Tests:**
  - Encrypt/decrypt round-trip for secrets; vault row cannot be read without DPAPI.
  - Schema creation idempotent; foreign keys enforced.
  - Token acquisition mock: simulate 429 and ensure retry/backoff.

### WP3 – OData Client & Metadata Cache
- **Goal:** Typed OData client for `/data` with metadata caching, URL builder, paging, and cross-company handling.
- **Refs:** toolbAX.md (Requirements > OData client, Query AST), this file.
- **Deliverables:** `src/FoToolbox.Core/OData/` with `IODataClient`, metadata cache, query builder; `tests/FoToolbox.Core.Tests/OData`.
- **Prompt for Codex:**
  ```
  Implement IODataClient for D365 F&O OData `/data`. Support $select,$filter,$orderby,$top,$skip,$count,$expand(1), cross-company. Parse and cache $metadata (ETag/version) in SQLite; expose entities/fields/enums. Provide QuerySpec -> URL builder honoring cross-company semantics (add cross-company=true when enabled; when disabled, apply dataAreaId/company filter if provided). Handle @odata.nextLink for paging.
  ```
- **Tasks:**
  - Metadata fetcher with ETag; store serialized model in SQLite; invalidate when ETag changes.
  - Query builder enforcing `$expand` depth 1; guard against unsupported operators.
  - HTTP client with `ResponseHeadersRead` and Polly-based retry/timeout.
- **Tests:**
  - Given sample metadata, surface entity and field lists (include enum types).
  - QuerySpec to URL examples:
    - Cross-company ON: `customers?$select=AccountNumber&cross-company=true`
    - Cross-company OFF + company `USMF`: includes `$filter=dataAreaId eq 'USMF'`.
  - Paging: mock @odata.nextLink traversal yields aggregated rows.

### WP4 – Query Builder Plugin (UI & AST)
- **Goal:** WPF plugin that visualizes entity/field selection, filter groups, ordering, paging, expand, cross-company toggle, preview grid, and saved queries.
- **Refs:** toolbAX.md (Priority plugin: Query Builder), WP3 outputs.
- **Deliverables:** `plugins/QueryBuilder/` project with MVVM views/viewmodels; `tests/Plugins.QueryBuilder.Tests/`.
- **Prompt for Codex:**
  ```
  Build the Query Builder plugin UI (WPF, MVVM). Left pane: entity/field tree from metadata cache. Center: filter builder with AND/OR groups and operators (eq, ne, gt, ge, lt, le, and, or, not, startswith, endswith, contains via wildcard hint). Right pane: options for $select, $orderby, $top/$skip, $count, $expand (single level with warning on deeper), cross-company toggle default ON, company dropdown. Bottom: preview grid bound to IODataClient paging; “Load more” follows @odata.nextLink; buttons for “Export page CSV” and “Export all CSV”.
  ```
- **Tasks:**
  - ViewModels for QuerySpec editing; validation hints for unsupported operators and $expand depth.
  - Persist saved queries via profile store (EnvId + JSON spec).
  - Preview grid with virtualization; show status/progress and errors inline.
- **Tests:**
  - QuerySpec serialization/deserialization matches saved query JSON.
  - Validation: $expand depth >1 triggers warning; contains renders wildcard.
  - VM-to-URL integration using mock IODataClient returns expected preview data.

### WP5 – CSV Export & Resilience
- **Goal:** Streamed CSV export (page or full) with back-pressure, cancellation, and retry.
- **Refs:** toolbAX.md (CSV export, Resilience), WP3.
- **Deliverables:** `src/FoToolbox.Core/Export/CsvExporter.cs`, wired into Query Builder plugin.
- **Prompt for Codex:**
  ```
  Implement CSV export for Query Builder results. Support current page and full dataset (iterate @odata.nextLink). Write UTF-8 with BOM, RFC-4180 quoting. Stream to FileStream with buffered writer; fetch next page only after prior flush (back-pressure). Support cancellation token, progress callbacks, and retry on 429/5xx with exponential backoff.
  ```
- **Tasks:**
  - CSV cell escape rules (`"` doubled, commas/newlines quoted).
  - Export pipeline uses IODataClient page stream; respects cancellation.
  - Error surface to UI (progress + failures).
- **Tests:**
  - Escaping: commas, quotes, CRLF.
  - Large export mock (100k rows) stays under memory threshold (assert via counted allocations or chunk size).
  - Cancellation stops mid-export; partial file is cleanly closed.

### WP6 – Packaging, Updates, and Plugin Trust
- **Goal:** MSI packaging (WiX), auto-update service, and plugin signature verification.
- **Refs:** toolbAX.md (Non-functional, Packaging), this file.
- **Deliverables:** `install/` WiX scripts, `src/FoToolbox.Updater/`, signing/config docs.
- **Prompt for Codex:**
  ```
  Create WiX-based MSI packaging for the host and plugins, including prerequisites (.NET Desktop Runtime). Add a background updater with channels (stable/beta), delta updates, and rollback on failure. Enforce plugin trust: verify Authenticode signature and manifest minSdk; refuse unsigned plugins by default with override toggle. Document signing process and update flow.
  ```
- **Tasks:**
  - WiX config with app GUID, start menu shortcut, and per-user install default.
  - Updater service to download signed packages, verify hash/signature, apply, and rollback.
  - Plugin load path checks signature thumbprint allowlist.
- **Tests:**
  - Updater rejects tampered package (hash mismatch).
  - Unsigned plugin blocked unless override flag enabled.
  - Install/uninstall preserves `profile.db`.

### WP7 – Test Harness & Fake OData Server
- **Goal:** Reusable test fixtures and fake OData server to validate client and plugin flows without real F&O.
- **Refs:** toolbAX.md (Validation plan), WP3–WP5.
- **Deliverables:** `tests/TestInfra/FakeODataServer/`, `tests/TestInfra/SampleMetadata.xml`, shared builders.
- **Prompt for Codex:**
  ```
  Provide a lightweight fake OData server (Kestrel/in-memory) that serves $metadata and paged entity data with @odata.nextLink. Include sample metadata covering enums, navigation properties, and company-aware entities. Expose helpers for IODataClient tests and Query Builder integration tests.
  ```
- **Tests:**
  - Fake server returns deterministic pages and nextLink sequencing.
  - Integration: Query Builder preview pulls from fake server and respects cross-company toggle.

Notes
-----
- Keep new files ASCII. Avoid introducing telemetry or network calls outside the fake server in tests.
- Prefer `rg` for search; follow repository structure above when creating projects.

Updates/Decisions (ongoing log)
-------------------------------
- Runtime: net8.0-windows (SDK pinned via global.json). MSAL auth wired with env-var fallback; vault-based secret resolution partially added (host checks SecretVault via ProfileStore; full integration backlog).
- Metadata: `$metadata` parsing supports properties and navigation properties; filter builder in Query Builder modeled after FetchXMLBuilder (AND/OR groups, add/remove).
- Export UX: CSV exports prompt for save location (SaveFileDialog), per user request.
- Query persistence: Query Builder saves queries per environment into ProfileStore (SQLite) with full query settings; names default to timestamp unless provided. Backlog: richer restore UI and ProfileStore-based prompts.
- Query Builder polish: filter builder now validates field/value pairs, shows wildcard hint, and enforces single-level `$expand` with navigation suggestions (ComboBox). Saved-query naming uses an in-app prompt (no VB InputBox) and will prompt to overwrite when a name already exists for the environment. Company filters are auto-appended when cross-company is off, even when a custom filter is provided.
- Query Builder paging: preview shows first page with a “Load more” action that follows `@odata.nextLink` and appends rows (button disabled when no nextLink; pagination resets when entity/fields/filter/order/expand/company toggles). Invalid expand paths are blocked before execution with a clear warning.
- WP4 backlog (not started): inline saved-query rename/delete controls with last-used auto-select, operator tooltips/examples, validation badges for company/cross-company conflicts, and inline error surfacing in the preview grid.
- WP5 backlog: reuse CsvExporter for any future export flows to centralize encoding/escaping behavior.
- WP6 (in progress): Plugin trust enforcement added with minSdk gating and Authenticode checks; unsigned plugins blocked unless `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS=true` (dev default). Thumbprint allowlist via `FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS`. Updater/WiX still to scaffold.
- WP6 updates: Added Updater skeleton (`FoToolbox.Updater`) with download + hash validation/staging and tests; WiX packaging placeholder docs added under `install/README.md`. Next: add channel config wiring and bundle .wxs with prereqs.
- WP6 channel flow: Update manifest loader + orchestrator added (channel-specific latest selection, hash-checked staging) with tests. Host reads `FOTOOLBOX_UPDATE_CHANNEL`/`FOTOOLBOX_UPDATE_MANIFEST` to stage updates on startup. Next: wire into host UI/settings and finalize WiX components/bundle for .NET Desktop Runtime.
- WP6 packaging: WiX fragments cover host dependencies and bundled plugins under `%LOCALAPPDATA%\FoToolbox\bin\` (plugins under `bin\plugins`) with an embedded-CAB MSI, plus a Burn bundle wired for .NET Desktop Runtime 8.0; still need to lock GUIDs/branding and sign outputs.
- WP6 updater UI: Host now exposes channel + “Check updates” in the main window; status surfaced via the status bar. Resilient fetcher added for update downloads.
- WP6 apply flow: “Apply staged update” now launches `msiexec /i <staged> /qb!` if a staged package exists. Rollback remains stubbed.
- WP6 packaging refresh: `profile.db` is created on first run under `%LOCALAPPDATA%\FoToolbox\bin\profile.db` and is not packaged into the MSI (so it survives upgrades/uninstall). Burn bundle chains .NET Desktop Runtime 8.0 via registry detection and variables `NetDesktopRuntimeVersion/Exe/Url` plus `FoToolboxMsiPath`. Remaining human actions: supply runtime installer path for builds, lock Product/Upgrade/Bundle codes and branding, and sign MSI/bundle.
