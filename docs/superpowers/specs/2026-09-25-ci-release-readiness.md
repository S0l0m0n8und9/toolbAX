# CI and release readiness (H04)

## Status and boundary

Implemented and locally validated; parent full gates, hosted CI, review and merge remain pending. No signing, package/SDK
version changes, machine-global runtime installation, tag creation/push or release publication is authorized.

Keep `build-test` and Linux `avalonia-tests` check names. Reusable CI and release verification use one
immutable source SHA; only publication receives write permission and it reuses the tested archive after
rechecking the existing tag. Pass raw tag input via environment/arguments, never executable-source interpolation.
Root verified the action pins below use Node 24; upload bundles with explicit `archive: true`. Hosted runners
only. Add shared Core to the App solution and explicitly enable WebView2 on Windows.

Smoke requires explicit absolute data/report paths before any App initialization. Permit absent/empty data,
reject nonempty data and reserve the report with CreateNew. Use normal real Windows composition, an
unactivated/taskbar-hidden native window, actual XAML/home readiness, zero profiles/active environment,
runtime assembly Release/provenance evidence and a WebView2 availability probe only. Capture startup and
background failures and finalize the report at shutdown. The driver owns its unique scratch root, sets the child
app-data override, runs the extracted executable, validates exit/report/provenance/readiness and kills only its
own process tree on timeout. No report-supplied path deletion or global installation. This is not live-auth,
portal, full-rendering or business-feature acceptance. Parent runs full solution/remote CI gates after freeze.

Verified baseline source facts (corrected by this implementation):

- Current CI runs Core tests on Windows and App tests only on Linux, omitting the Windows/WebView2 path.
- `release.yml` packages an executable/zip without the normal test suites.
- The App `.slnx` omits shared Core; its Release build currently emits the referenced Core under Debug.
- `ProfilePaths.FOTOOLBOX_APPDATA_DIR` isolates the profile DB and `SessionTraceLog`; fresh Windows startup has no profiles or active environment, and home does not acquire tokens or issue tenant requests.

## Target state

A reusable verification workflow is called by PR/main CI and release. It runs Core Windows tests; App Linux and Windows tests, with Windows explicitly `EnableWebView2=true`; a shipping-graph vulnerability check that fails on command/audit failure; Release configuration consistency; and a smoke test of the extracted published win-x64 archive before publication.

Implementation should add Core to the App solution, or prove the smallest equivalent, and assert the shipped runtime assembly is Release-built. Preserve the portable zip and SHA256; the archive tested is the archive published.

## Release provenance and permissions

Resolve an existing validated `v<major>.<minor>.<patch>[-prerelease][+metadata]` tag once to an immutable SHA. Build, test, package, and publish that SHA only. Any failed, skipped, or cancelled required job blocks publication. Recheck tag/SHA consistency before publishing the tested artifact.

Pass raw workflow input through environment variables or structured arguments, never interpolated into PowerShell source. Derive numeric `FileVersion` separately from semantic/package version. Validation/build jobs have read-only token permissions; only final publication has `contents:write`. Do not create/push tags or execute publishing in H04. The unsigned boundary remains explicit.

Root-verified immutable action pins:

- `actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1` (v7.0.1)
- `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68` (v6.0.0)
- `actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` (v7.0.1)
- `actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c` (v8.0.1)

Keep the existing pinned `softprops` action unless separately justified.

## Package smoke

A narrow `--smoke-test` entry path and helper script launch the extracted release executable with a fresh, driver-owned data directory and report path. The driver sets `FOTOOLBOX_APPDATA_DIR` before launch and passes it as an override even for an older mismatched executable. It rejects nonempty data directories and existing report files rather than overwriting them; all outputs stay under its known scratch root.

The smoke launches the actual archive executable with `WindowStyle Hidden`, no taskbar/focus steal using supported Avalonia options, waits to a finite deadline, and terminates only its own process on timeout. It validates real Avalonia desktop startup/window/XAML and Windows service composition, MainWindow readiness, expected home/tool availability, empty profiles, Release configuration, and WebView2 compile capability. It probes native-loader/runtime availability without opening a browser or navigating. It fails on degraded/fake composition, startup/background error, wrong/missing report, nonzero exit, or timeout, retaining useful evidence and classifying prerequisite versus packaging failure.

The JSON report and CLI isolation lifecycle need meaningful tests. The smoke is startup/package evidence only, not live authentication, portal, or full GUI acceptance. Do not infer WebView2 Runtime from a bundled .NET runtime; hosted CI prerequisites may be declared only from official documentation. No machine-global runtime installation locally without explicit approval.

## Pre-mortem

1. Linux-green hides a Windows adapter/native defect. Mitigate with Windows App build/test and actual packaged startup.
2. Smoke touches credentials or silently uses fake fallback. Require fresh isolated data, zero-profile/real-composition assertions, and no auth calls.
3. A moved tag or failed check publishes different bits. Gate immutable SHA, dependencies, tested-artifact reuse, and final tag consistency.
4. Malicious tag input executes shell text or build jobs gain write authority. Constrain validation, use env/arguments, and apply least permissions.
5. A hung/crashed/partial app appears successful. Use an own-process watchdog, readiness/report/exit checks, and retained evidence.

## Acceptance before implementation completion

Require CLI isolation/report lifecycle tests, release-tag validation tests, local Windows package smoke, workflow validation, and observed PR CI on Linux and Windows. Release publication remains unexecuted unless separately authorised.

## Local verification

At source `1cc22ff786d9ecd5c52d73700889212893e797fb`, the local Windows self-contained publish, zip, fresh extraction and real desktop smoke exited 0. The report records real composition, loaded MainWindow/home, every expected tool, zero profiles/no active environment, and all three runtime assemblies in Release with that exact source SHA. WebView2 availability reported `154.0.4258.37`; no browser, navigation or sign-in was created and no runtime was installed.

Focused smoke tests passed 26/26 after meaningful RED (25 failures, one normal-launch control passed). Release-helper checks passed 44/44; the audit-shape regression first failed 11 cases, then accepted genuine clean path-only project reports while rejecting null/empty/malformed collections. The actual shipping dependency audit passed with no stderr. The App solution Release build emitted shared Core in Release with zero warnings/errors. Official workspace-local actionlint 1.7.12 accepted both workflows. All XML/config assets remain in the archive; only PDBs are excluded.

Evidence is retained under ignored `artifacts/h04/`: `smoke-integrated-green.trx`, `audit-shape-{red,green}.log`, `shipping-audit-final.json`, `actionlint-final.log`, `native-package-final.log`, and `package-final/smoke-dc4e6de8134447adbee1e545bfbe34fb/report.json`. This proves local startup/package readiness only. Hosted Windows WebView2 availability, Linux/Windows remote jobs, full solution gates and release publication are not claimed by this checkpoint. Parent will integrate H15 and perform final gates before delivery. Distribution remains unsigned and requires an available WebView2 Runtime.
