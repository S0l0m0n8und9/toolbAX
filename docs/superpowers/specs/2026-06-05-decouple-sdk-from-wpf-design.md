# Decouple the plugin SDK from WPF UI types — design (#33)

Status: **design / scoping** (no production code yet). Phase 2 of the modernization track
(#32 modernise shell → **#33 decouple SDK** → #35 alternate-UI-host spike). #33 is the prerequisite
that makes #35 possible: while the plugin contract hard-references WPF, no non-WPF host can load a
plugin.

## Goal

Make `FoToolbox.SDK` (the public plugin contract) **not depend on WPF**, so:

1. an alternate UI host (#35) can consume the same plugins, and
2. plugin authors are not *forced* by the contract to return a WPF type (existing plugins may keep
   using WPF — this is about the *contract*, not banning WPF).

Non-goal: rewriting any plugin's UI, or shipping an alternate host. Just remove WPF from the SDK seam
and leave a clean adapter in the WPF host.

## Current coupling (inventory)

Only three things tie the SDK to WPF:

| # | Location | Coupling |
|---|----------|----------|
| 1 | `IFoToolPlugin.CreateTool()` | returns `System.Windows.Controls.UserControl` |
| 2 | `Commands/PluginCommands.cs` (`AsyncRelayCommand`, `RelayCommand`) | implement `System.Windows.Input.ICommand` |
| 3 | `FoToolbox.SDK.csproj` | `<UseWPF>true</UseWPF>` (needed today only because of #1) |

### How the control flows today

```
plugin.CreateTool()  ──► UserControl
  PluginManager.LoadPluginAsync: var control = plugin.CreateTool();
  └► LoadedPlugin.ToolControl   (UserControl, host)
     └► MainWindowViewModel.PluginEntry.Control   (UserControl)
        └► ActiveControl => Selected?.Control
           └► <ContentControl Content="{Binding ActiveControl}"/>   (WPF renders it)
```

The host is the only consumer of the control, and it consumes it purely as "something a
`ContentControl` can host" — i.e. as a `FrameworkElement`. The SDK contract is more specific
(`UserControl`) than the host actually needs.

### What is *not* coupled

`IPluginContext`/`IPluginContextWrite`/`…`, the manifest, `Version`, `InitializeAsync` — all already
UI-agnostic. Note `System.Windows.Input.ICommand` is defined in `System.ObjectModel` on modern .NET
(not `WindowsBase`), so coupling #2 likely does **not** require `UseWPF` once #1 is removed — to be
verified in Phase 1 by building the SDK with `UseWPF=false`.

## Design options for the `CreateTool()` seam

- **A. `object CreateTool()`** — host casts to `FrameworkElement`. Minimal, but loses all type info
  and gives plugin authors no signal of intent.
- **B. Marker interface `IPluginView` (SDK-defined, no UI members) + `IPluginView CreateTool()`** —
  the WPF host adapts an `IPluginView` to the actual visual. Keeps a typed contract, documents intent,
  and lets a future host define its own adapter. The WPF plugins return a small wrapper (or the SDK
  ships a WPF-side adapter) so existing `UserControl`s flow through unchanged.
- **C. View descriptor / factory** — plugin returns data describing the view; host builds it. Largest
  change; only worth it if we expect fully declarative, cross-framework UIs (we don't, yet).

**Recommendation: B**, with the lightest possible marker. The contract returns a UI-agnostic
`IPluginView`; the WPF host owns a single adapter (`IPluginView` → `FrameworkElement`). For the common
case, the SDK provides a `WpfPluginView` (in a *separate* `FoToolbox.SDK.Wpf` assembly that keeps
`UseWPF=true`) wrapping a `UserControl`, so existing plugins change one line
(`return new WpfPluginView(control);`) and the core `FoToolbox.SDK` drops its WPF dependency entirely.

This splits the contract (UI-agnostic, in `FoToolbox.SDK`) from the WPF convenience (in
`FoToolbox.SDK.Wpf`), which is the clean long-term shape for #35.

## Phased plan

- **Phase 1 — introduce the seam (safe first step).**
  - Add `IPluginView` (empty marker) to `FoToolbox.SDK`.
  - Change `CreateTool()` to return `IPluginView`.
  - Add `FoToolbox.SDK.Wpf` (`UseWPF=true`) with `WpfPluginView(UserControl)` + an
    `IPluginView`→`FrameworkElement` accessor used by the host.
  - Host: `PluginManager` resolves the `FrameworkElement` from the returned `IPluginView`;
    `LoadedPlugin.ToolControl`/`PluginEntry.Control`/`ActiveControl` widen `UserControl`→`FrameworkElement`.
  - Set `UseWPF=false` on `FoToolbox.SDK` and confirm `ICommand` still resolves.
  - Each plugin: `return new WpfPluginView(theUserControl);` (one line).
  - **Backwards-compat:** all plugins are first-party here (rebuilt together), so a contract change is
    acceptable; no third-party plugins to break. Trust/strong-naming unaffected (new assembly is
    signed with the same key).
- **Phase 2 — command contract.** If `ICommand` needs no WPF (expected), leave as-is; otherwise add a
  framework-neutral command base. Confirm `FoToolbox.SDK` has zero WPF references.
- **Phase 3 — enable the alternate host (#35).** A non-WPF host defines its own `IPluginView` adapter;
  validates a plugin loads without `PresentationFramework`.

## Safe first step (recommended starting PR)

Implement **Phase 1** only, as one PR touching the SDK, the new `FoToolbox.SDK.Wpf`, the host's plugin
loading/binding, and the seven plugins' `CreateTool` one-liner. It is mechanical, fully covered by the
existing `FoToolbox.UiTests` (every view still mounts) + `PluginManagerTests` (every plugin still
loads), and leaves behaviour identical while removing WPF from the core contract.

## Risks / trade-offs

- **Public contract break:** `CreateTool` signature changes. Acceptable — all plugins are first-party
  and rebuilt in lockstep; document in the SDK changelog. (If external plugins ever exist, ship a
  default `CreateTool()` shim.)
- **Two SDK assemblies:** `FoToolbox.SDK` (agnostic) + `FoToolbox.SDK.Wpf` (convenience). Both must be
  staged into each plugin folder by the installer (`FoToolboxFiles.wxs`) and `build.ps1` — see the
  installer notes; missing a file is not caught by tests.
- **Marshalling:** none — the WPF host still creates/renders on the UI thread exactly as today.

## Test impact

- `FoToolbox.UiTests` (binding harness) and `PluginManagerTests` already exercise every plugin's
  view + load path; they become the Phase 1 regression net.
- Add an SDK-level test asserting `FoToolbox.SDK` carries **no** reference to `PresentationFramework`
  (guards the decoupling from regressing).
