# Layer C — End-to-End UI Tests via FlaUI (Design)

**Date:** 2026-06-04
**Status:** Approved (user delegated all decisions to recommended options; running autonomously)
**Scope:** Layer C of the three-layer headless UI testing strategy. Layer A (view↔VM
wiring) shipped in PR #36. Layer B (visual regression) is dropped.

## Problem

Layer A proves every view *constructs* and binds correctly offscreen, but it never runs
the **real application** — App startup, the shell, plugin/profile loading, and actual
user interaction (clicks, typing, navigation) are untested. Layer C drives the built
`FoToolbox.Host.exe` like a user, via **FlaUI** (UI Automation), and asserts real outcomes.

## Goal & contract

Launch the real app on a Windows desktop session, drive it through a small set of
**network-free, auth-free** user flows, and assert the app reaches and responds in the
expected UI states. Every flow must be deterministic and self-cleaning.

## Decisions (all recommended options; user pre-approved)

| Decision | Choice |
| --- | --- |
| Tool | **FlaUI** (`FlaUI.UIA3` + `FlaUI.Core`), MIT, no external driver. Via CPM. |
| Project | New `tests/FoToolbox.E2eTests` (`net10.0-windows`). Drives the built exe; no runtime ProjectReference to Host. |
| Launch determinism | Child process with env overrides — **no production code change**. |
| Flows (v1) | (1) launch smoke + shell present + clean exit; (2) Profiles add-form input + validation. |
| Deferred | Plugin-tab navigation (needs an active profile → profile DB seeding). Live auth/data flows (out of scope, as in Layer A). |
| AutomationIds | Minimal targeted pass on shell + ProfilesView elements the flows touch. |
| CI | Separate `e2e-tests` job, **advisory (`continue-on-error: true`)** until proven stable. |
| Reliability | FlaUI `Retry`/`WaitUntil` with timeouts (no `Thread.Sleep`); app launched/closed per test via fixture; screenshot-on-failure to CI artifacts. |

## Deterministic launch recipe (from Host startup investigation)

The built `FoToolbox.Host.exe` reaches a usable `MainWindow` (title **`toolBax`**) with no
network, no dialogs, no auth, and isolated data when launched with these process env vars:

- `LOCALAPPDATA` = a fresh temp directory per test run → isolates `profile.db`, secret
  vault, catalog cache, trust store, logs (paths derive from `%LOCALAPPDATA%/FoToolbox/`).
  No code hook needed; overriding the child process env is sufficient.
- `FOTOOLBOX_UPDATE_MANIFEST` = empty → the (already fire-and-forget) update check is a
  no-op; zero network.
- Bundled plugins are strong-named → auto-trusted, **no consent dialog**. No third-party
  plugins exist in a clean build, so `PluginConsentWindow` never appears.

With no profile in the fresh `profile.db`, the app shows only the **Profiles** tab in a
"No profile" state — reachable and interactive without credentials. No MSAL/token work
happens until an HTTP request (profile activation/query), which these flows never trigger.

Clean exit (`OnClosed`) cancels in-flight tasks and disposes HTTP clients with no blocking
work, so teardown is fast.

## Design

### 1. Project & dependencies

New `tests/FoToolbox.E2eTests/FoToolbox.E2eTests.csproj`:
- `net10.0-windows`, `IsTestProject=true`, signed with `build/fotoolbox.snk` (matches other
  test projects), `NoWarn;CS8002`.
- Packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
  `coverlet.collector`, **`FlaUI.UIA3`** (pulls in `FlaUI.Core`). Add `FlaUI.UIA3` version to
  `Directory.Packages.props` (CPM).
- **No runtime ProjectReference to Host** (we launch the exe, not load the assembly). A
  build-order-only `ProjectReference` to `FoToolbox.Host` with
  `ReferenceOutputAssembly=false` ensures the exe is built before the tests when building
  this project directly.
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` — only one app
  instance / desktop at a time.

### 2. Harness components

- **`AppDriver` (`IDisposable`)** — locates `FoToolbox.Host.exe` (walk up from
  `AppContext.BaseDirectory` to repo root, then
  `src/FoToolbox.Host/bin/{Configuration}/net10.0-windows/FoToolbox.Host.exe`; throw a
  clear "build the solution in {config} first" error if absent). Launches it via FlaUI
  `Application.Launch` with a `ProcessStartInfo` carrying the env overrides above and a
  unique temp `LOCALAPPDATA`. Exposes the main `Window` (found by title `toolBax` with a
  bounded `Retry`). On dispose: close the app, wait for exit (bounded), delete the temp
  `LOCALAPPDATA` dir (best-effort), dispose the `UIA3Automation`.
- **`E2eFact`** — wrapper attribute (or a `[Fact]` + `Skip` guard) that **skips** when no
  interactive desktop session is available (`Environment.UserInteractive == false` or a
  documented env flag), so the suite degrades gracefully on headless agents instead of
  hanging/failing. CI `windows-latest` is interactive, so it runs there.
- **Screenshot-on-failure** — a small helper captures the desktop/window to a file under a
  results dir when an assertion fails, for CI artifact upload and triage.

### 3. Flows (v1)

- **`AppLaunchTests.App_launches_to_main_window_and_exits_cleanly`** — launch via
  `AppDriver`; assert the main window (title `toolBax`) appears within a timeout; assert the
  **Profiles** navigation entry is present; dispose; assert the process exited.
- **`ProfilesFlowTests.Add_profile_form_validates_input`** — from the launched app, open the
  Profiles view, activate the add-profile affordance, type into the environment name / URL
  fields, and assert the form reflects input and surfaces a validation message for invalid
  input — **without saving/connecting** (no network). Exact assertions follow the real
  ProfilesView once AutomationIds are added.

### 4. AutomationId pass (production XAML, additive only)

Add `AutomationProperties.AutomationId` to exactly the elements the v1 flows select:
- `MainWindow.xaml`: the window (`FoMainWindow`) and the Profiles navigation entry / left-rail (`FoProfilesNavItem` / `FoNavRail`).
- `ProfilesView.xaml`: the add-profile button, the environment name + base-URL inputs, and
  the validation/error text element.

These are additive automation hints; they change no behavior. (Layer A's binding-error
suite guards against regressions in these views.)

### 5. CI

New **`e2e-tests` job** in `.github/workflows/ci.yml` (windows-latest, interactive session):
build the solution (or Host) in Release, then `dotnet test tests/FoToolbox.E2eTests` with
the launch env vars set, `--blame-hang`, and a results dir. **`continue-on-error: true`** —
advisory until the suite proves stable over several runs (E2E driving a real GUI is
inherently flakier than unit/headless tests; we don't let it block merges to `main` yet).
Upload the results + failure screenshots as an artifact. `build-test` and `ui-tests` are
unaffected (each targets its own project).

### 6. Out of scope

- Plugin-tab navigation / any flow needing an active profile (requires seeding `profile.db`
  — a follow-on once launch + Profiles flows are proven).
- Live auth / OData / Dataverse / WebView2 sign-in (no backend; same exclusion as Layer A).
- Visual/bitmap assertions (Layer B, dropped).

## Risks & mitigations (accepted)

- **GUI flakiness / runner variance** → advisory CI job, bounded `Retry`/`WaitUntil`,
  screenshot-on-failure, serialized execution.
- **Local session lock overnight** can break UI Automation → the `E2eFact` skip-guard
  prevents hard failures when no interactive session is present; CI remains the canonical
  run environment.
- **Exe path / build layout** → `AppDriver` resolves the path explicitly and fails with a
  clear "build first" message; CI builds before running.

## Relationship to the wider strategy

Layer A (PR #36, merged) + this Layer C complete the agreed A→C plan. B remains dropped.
The deferred profile-seeded plugin-navigation flows are the natural next increment after
this lands.
