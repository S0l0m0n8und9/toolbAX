# Layer A — Headless View↔VM Wiring Tests (Design)

**Date:** 2026-06-03
**Status:** Approved (design); implementation plan pending
**Scope:** Layer A of a three-layer headless UI testing strategy. Layers B (visual
regression) and C (end-to-end via FlaUI) are out of scope here — see "Relationship to
the wider strategy" below.

## Problem

The toolbAX WPF host and its plugins have well-tested ViewModels (pure xUnit), but the
**Views themselves are barely exercised**. Two existing tests hand-roll an STA thread to
`new MainWindow()` / instantiate a view, but none of them:

- run a view's full production lifecycle and assert it constructs without throwing,
- detect WPF data-binding failures (broken binding paths, missing resources, faulty
  converters / `DataTemplate`s).

These failures are invisible to ViewModel tests because the View is never instantiated.
They surface only at runtime, in front of a user, as silent broken UI or trace-window
spew.

## Goal & contract

For every host view and plugin tool control, prove — with no visible window — that it:

1. **Constructs without throwing** — XAML parses, resources resolve, the real plugin
   lifecycle (`InitializeAsync` → `CreateTool`) completes.
2. **Produces zero WPF data-binding errors** after layout.

This is a deliberately **uniform, low-maintenance contract**: the same two assertions
apply to every view, so adding a view costs ~one line and there is almost no per-view
logic to rot. It catches the entire class of binding-path typos, missing resources, and
converter/`DataTemplate` faults.

### Known limitation (accepted)

"Zero binding errors" only covers bindings that actually evaluate. An idle ViewModel
leaves data-template and item bindings unexercised (an `ItemsControl` over an empty
collection never instantiates its item template). The `WarmUp` escape hatch (§4) raises
coverage for the data-heavy views without forcing per-view setup on all of them.

## Decisions (locked during brainstorming)

| Decision | Choice |
| --- | --- |
| Coverage | Host views **+ all plugin tool controls** |
| Assertions | **Construct + zero binding errors** (uniform contract) |
| DataContext | **Real lifecycle + seeded data** (run `InitializeAsync`/`CreateTool`; seed fakes) |
| Test location | **New `FoToolbox.UiTests` project + separate CI job** |
| Harness approach | **Approach 1**: `Xunit.StaFact` + offscreen `HwndSource` + binding `TraceListener` |

## Context discovered

- `IPluginContext` is small: `CurrentEnv`, `OData` (streaming reads), `Catalog`, `Logger`.
  Three optional cast interfaces: `IPluginContextWrite`, `IPluginContextDataverse`,
  `IPluginContextNavigation`.
- `FakeContext` / `FakeODataClient` / `FakeCatalogService` already exist but are
  **duplicated** across `QueryBuilderPluginTests`, `DualWriteOperationsViewModelTests`,
  and others.
- `QueryBuilderPluginTests` already runs `InitializeAsync` on a hand-rolled STA thread
  but stops short of `CreateTool()` + layout + binding-error assertions.
- **WebView2 lives only in `DualWriteSignInWindow`** — a transient auth window shown on
  demand, *not* returned by any `CreateTool()`. All 7 plugin tool controls are
  WebView2-free and safe to mount offscreen.
- CI already runs on `windows-latest`, which provides a real desktop session — a hard
  prerequisite, since WPF has no true display-less renderer.

## Design

### 1. Project & dependencies

New project **`tests/FoToolbox.UiTests/FoToolbox.UiTests.csproj`**:

- `net10.0-windows`, `UseWPF=true`, `IsTestProject=true`.
- Signed with `build/fotoolbox.snk` (matches `FoToolbox.Tests`).
- Project references: `FoToolbox.Host` + all 7 plugins (HelloPlugin, QueryBuilder,
  TableEntityBrowser, ODataPostBuilder, DualWriteMapBrowser, DualWriteOperations,
  DualWriteCompare).
- Packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
  `coverlet.collector`, **`Xunit.StaFact`** (MIT). Add the `Xunit.StaFact` version to
  `Directory.Packages.props` (CPM) — not to the csproj.
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` — the binding
  `TraceListener` and the WPF `Application` are process-global, so UI tests run
  sequentially.

### 2. Harness components

Each component is small and single-purpose.

- **`UiTestApplicationFixture`** (xUnit collection fixture) — lazily creates the single
  WPF `Application` on the StaFact dispatcher and merges the host's theme/resource
  dictionaries so `StaticResource` / `DynamicResource` lookups resolve.
- **`BindingErrorScope : IDisposable`** — attaches a `TraceListener` to
  `PresentationTraceSources.DataBindingSource` at `SourceLevels.Warning`, collects
  messages, detaches on dispose. Exposes `IReadOnlyList<string> Errors`.
- **`OffscreenHost`** — mounts a `FrameworkElement` in an invisible `HwndSource` (a real
  `PresentationSource`, so `Loaded` fires and styles apply), runs
  `Measure`/`Arrange`/`UpdateLayout`, pumps the dispatcher to `ApplicationIdle`.
  Disposable; tears down the `HwndSource`.
- **`FakePluginContext`** — one seeded fake implementing `IPluginContext` **+**
  `IPluginContextWrite` + `IPluginContextDataverse` + `IPluginContextNavigation`.
  Consolidates the fakes currently duplicated across `FoToolbox.Tests`. Seeded `Catalog`
  (a few tables/entities) and `OData` (a representative page of rows); write client
  no-ops; `HasDataverseProfile = false`; `TryNavigateTo` returns `false`.

### 3. View registry & the seeded-data warm-up

A `ViewCase` record:

```
record ViewCase(string Name, Func<Task<UserControl>> Factory, Action<object?>? WarmUp);
```

- **Plugin cases** — an explicit list of the 7 plugin types; the factory does
  `new Plugin()` → `InitializeAsync(ctx)` → `CreateTool()`. Explicit (not reflection)
  keeps it trust-safe and readable.
- **Host cases** — explicit factories for `ProfilesView` (+ a real `ProfilesViewModel`
  built from fakes) and `PluginConsentWindow`'s content.
- **`WarmUp`** is the one non-uniform escape hatch: an optional callback that invokes a
  view's primary load command so seeded data flows into item/data-template bindings,
  after which the harness pumps again. **Most cases leave it `null`**; only data-heavy
  views (e.g. QueryBuilder, DualWriteMapBrowser) get a one-liner.

### 4. The test

A single `[WpfTheory]` over the registry:

```csharp
using var scope = new BindingErrorScope();
var ctrl = await c.Factory();               // construct + lifecycle (throws => fail)
using var host = OffscreenHost.Mount(ctrl);
c.WarmUp?.Invoke(ctrl.DataContext);         // optional: load seeded data
host.PumpToIdle();
Assert.Empty(scope.Errors);                 // zero binding errors
```

The factory throwing satisfies assertion (1); the empty-errors check satisfies (2).

### 5. CI

A new **separate job `ui-tests`** in `.github/workflows/ci.yml` (windows-latest):
restore → build → `dotnet test tests/FoToolbox.UiTests` with `--blame-hang`. Kept
isolated from the `build-test` job so a flaky UI run never masks the unit-test signal.

**Quarantine policy:** any view that proves genuinely flaky goes into an explicit,
commented skip list with a tracking note — never weaken the assertion for every view to
accommodate one.

### 6. Out of scope

- `DualWriteSignInWindow` (WebView2 + live auth).
- Visual / bitmap snapshot assertions — Layer B, dropped (management cost too high).
- Driving the running application — Layer C, a separate follow-on spec.

## Relationship to the wider strategy

This is **Layer A** of three. Sequencing agreed: **A → C**, B dropped.

- **A (this spec):** View↔VM wiring + binding-error safety net. Reliable foundation.
- **C (follow-on spec):** End-to-end user flows via **FlaUI** (MIT, no external driver).
  Needs a separate project, a dedicated CI job, and an `AutomationId` pass over the XAML.
  To be brainstormed after Layer A is proven.
- **B (dropped):** Render-to-bitmap visual regression. Free to build but its baselines
  rot across machines (ClearType/DPI/font drift); its *management* cost fails the
  "low-maintenance" filter.

## Testing the harness itself

- A deliberately-broken view fixture (a control with a known-bad binding path) confirms
  `BindingErrorScope` actually catches errors — otherwise the suite could be green
  because it detects nothing.
- A trivially-correct control confirms the happy path reports zero errors and does not
  hang.
