# toolBax UI Refresh — Design Spec

**Date:** 2026-05-27
**Owner:** Ben Jones
**Status:** Draft (awaiting review)
**Scope:** Polish + consistency pass over the WPF host shell and plugin chrome. No new screens, no plugin-contract breakage, no new themes.

---

## 1. Background

`FoToolbox.Host` is a WPF Windows desktop application (.NET 9) that hosts plugin `UserControl`s for working with Dynamics 365 F&O. The v2.0 "warm-dark terminal" palette landed recently (commit `6d63b03`) and the foundation is good, but several inconsistencies and gaps remain:

- Host uses `CornerRadius=2`; plugin cards use `CornerRadius=6` — two visual vocabularies.
- `ProfilesView` uses legacy `GroupBox` while plugins use modern `Border` cards.
- No global surface for the active profile or connection state.
- Icon SVG path data is hard-coded inside `MainWindowViewModel` (concerns coupled, plugins can't supply custom icons).
- The theme file is named `Fluent.Light.xaml` but contains the warm-dark theme.
- Status bar is minimal: no busy indicator, no plugin status surface.
- Updater control bar lives between content and status — awkward vertical placement.
- Each plugin invents its own toolbar layout (mix of `WrapPanel`, `Grid`, `StackPanel` with ad-hoc spacing).

## 2. Goals

1. Unify visual language between host chrome and plugin views (sharp 2px radii everywhere, shared spacing tokens, one toolbar style).
2. Surface the active profile globally via a clickable title-bar chip that supports inline switching.
3. Modernize `ProfilesView` to match the plugin card aesthetic and reduce vertical scrolling.
4. Make the status bar functionally useful (active plugin, profile echo, busy state, last-connection timestamp, update indicator).
5. Move updater controls into a discreet title-bar overflow menu instead of a dedicated bar.
6. Decouple icon data from the view-model; allow plugins to opt into named icons via manifest.
7. Rename the misnamed theme file.

## 3. Non-goals

- No light theme, no high-contrast theme.
- No command palette / Ctrl+K.
- No notification drawer or toast host.
- No docking / `AvalonDock` introduction.
- No plugin contract breaking changes — the new manifest icon field is optional.
- No new external dependencies.
- No new persistence model for profiles (Save button stays explicit).

## 4. Architecture

### 4.1 File layout (new and renamed)

```
src/FoToolbox.Host/
  Themes/
    Fluent.Theme.xaml          (RENAMED from Fluent.Light.xaml)
    Fluent.Controls.xaml       (refined — toolbar style + chip style + status pip)
    Icons.xaml                 (NEW — Geometry resources)
    Spacing.xaml               (NEW — Fo.Space.* tokens as Thickness/Double)
  Controls/
    PluginToolbar.cs           (NEW — Custom Control deriving from ItemsControl)
    ProfileChip.cs             (NEW — Button-derived with attached Popup)
    StatusPip.cs               (NEW — Control with State enum)
  ViewModels/
    AppShellViewModel.cs       (NEW — owns active profile, busy aggregation,
                                 connection status, navigation requests)
    MainWindowViewModel.cs     (refactored — thinner; composes AppShellVM and
                                 plugin list; no more IconPathFor)
    ProfilesViewModel.cs       (EXISTS — extended with TabSelection + active-marker)
    IconResourceResolver.cs    (NEW — static helper: manifest key → Geometry)
  Views/
    ProfilesView.xaml          (rewritten — list + tabbed detail + sticky toolbar)
    ProfilesView.xaml.cs       (slimmed — only WPF-required handlers)
  MainWindow.xaml              (refreshed title bar, status bar, removed update bar)

src/FoToolbox.SDK/
  IPluginBusyState.cs          (NEW — optional opt-in: bool IsBusy { get; }
                                 + event PropertyChanged)

tests/FoToolbox.Host.Tests/    (NEW test project if absent, else add tests)
  IconResourceResolverTests.cs
  AppShellViewModelTests.cs
  ProfileChipTests.cs
```

### 4.2 View-model split rationale

Today `MainWindowViewModel` carries three responsibilities: plugin discovery + selection, icon mapping, and update orchestration. Cross-cutting state (active profile, aggregate busy, connection status) does not have a home. The split:

- **`AppShellViewModel`** — cross-cutting shell state. Single source of truth for: `ActiveProfile`, `Profiles` (read-only projection), `IsBusy` (aggregate), `ConnectionStatus` (`Unknown`/`Ok`/`Warning`/`Error`), `LastPingAt` (nullable timestamp). Raises `NavigateToProfilesRequested` event for the title-bar chip to drive plugin switching without coupling to the plugin collection.
- **`MainWindowViewModel`** — composition. Holds the plugin collection, the active control, the updater pieces, and exposes `Shell` (the `AppShellViewModel` instance) for view binding.
- **`ProfilesViewModel`** — already exists; we extend it with a `TabSelection` enum property and an `IsActive(profile)` helper for the active-profile marker in the list. No structural lift required.

### 4.3 Icon system

`Themes/Icons.xaml` holds `Geometry` resources keyed `Icon.Profiles`, `Icon.Query`, `Icon.DualWrite`, `Icon.TableEntity`, `Icon.ODataPost`, `Icon.Settings`, `Icon.Plugin` (default fallback). Path data sourced from the existing strings in `MainWindowViewModel.IconPathFor`.

Plugin manifest gains an optional string field:
```json
{ "icon": "Query" }
```

`IconResourceResolver.Resolve(manifest)` resolution chain:
1. Explicit `manifest.Icon` → `Application.Current.TryFindResource("Icon." + manifest.Icon)`.
2. Name heuristic on `manifest.Name` (today's behaviour, preserved as fallback).
3. `Icon.Plugin` default.

Returns `Geometry`. The XAML binding shifts from `{Binding IconPath}` (string) to `{Binding IconGeometry}` (Geometry on `PluginEntry`).

### 4.4 Plugin busy interface

```csharp
// FoToolbox.SDK
public interface IPluginBusyState : INotifyPropertyChanged
{
    bool IsBusy { get; }
}
```

`AppShellViewModel.IsBusy` is `Plugins.Any(p => p.BusyState?.IsBusy == true)`. Plugins that already expose an `IsBusy` boolean (QueryBuilder, TableEntityBrowser, DualWriteMapBrowser, ODataPostBuilder all do) implement the interface in a single line. Plugins that do not implement it contribute nothing — the shell treats them as idle.

This is the minimum coupling needed for the shell to show a global busy spinner without rewriting plugin VMs.

## 5. Visual / design tokens

### 5.1 Radii

Both `Fo.CornerRadius.Card` and `Fo.CornerRadius.Control` = `2`. Plugin views currently using `CornerRadius="6"` on `Border` cards are migrated to use `{DynamicResource Fo.CornerRadius.Card}` (yielding 2).

### 5.2 Spacing (`Spacing.xaml`)

```xml
<sys:Double x:Key="Fo.Space.2">2</sys:Double>
<sys:Double x:Key="Fo.Space.4">4</sys:Double>
<sys:Double x:Key="Fo.Space.6">6</sys:Double>
<sys:Double x:Key="Fo.Space.8">8</sys:Double>
<sys:Double x:Key="Fo.Space.12">12</sys:Double>
<sys:Double x:Key="Fo.Space.16">16</sys:Double>
<sys:Double x:Key="Fo.Space.24">24</sys:Double>

<Thickness x:Key="Fo.Margin.Card">8</Thickness>
<Thickness x:Key="Fo.Padding.Card">12</Thickness>
<Thickness x:Key="Fo.Margin.FormRow">0,10,0,0</Thickness>
```

Plugin XAML files have their ad-hoc literal margins (e.g. `"8,10,0,4"`) updated to reference these tokens during the refresh. Non-blocking — literals that don't match the scale are left alone.

### 5.3 Typography

No new font sizes; the existing scale in `Fluent.Theme.xaml` is sufficient. The refresh enforces consistent use:

- `Fo.FontSize.Heading` (15) — screen titles only ("Profiles", "Settings").
- `Fo.FontSize.SubHeading` (14) — card titles ("FO Environment", "Tables").
- `Fo.FontSize.Body` (12) — default.
- `Fo.FontSize.Small` (11) — secondary labels under inputs.
- `Fo.FontSize.Caption` (10) — status bar.

### 5.4 Theme rename

`App.xaml` references `Themes/Fluent.Light.xaml`. Rename file to `Themes/Fluent.Theme.xaml`, update the merged dictionary reference. No plugin XAML references the file directly (they consume `DynamicResource` keys), so no plugin update is required.

## 6. Components

### 6.1 `ProfileChip` (title-bar control)

- Visual: `[●] PROD-NZ ▾`. Filled colored dot reflects `ConnectionStatus`: `Ok` → `Fo.SuccessBrush`, `Warning` → `Fo.WarningBrush`, `Error` → `Fo.ErrorBrush`, `Unknown` → `Fo.SubtleTextBrush` (hollow ring).
- DPs: `Profiles` (IEnumerable), `ActiveProfile` (object), `ConnectionStatus` (enum). Commands: `SetActiveProfileCommand`, `OpenProfilesCommand`.
- Left-click opens a `Popup` (max-height 320, scrolls) listing profiles by name; click a row → invokes `SetActiveProfileCommand` and closes.
- Right-click invokes `OpenProfilesCommand` (navigates the shell to the Profiles tab).
- When `ActiveProfile == null`: text reads `No profile`, dot is hollow, dropdown shows only one row "Set up a profile…" which invokes `OpenProfilesCommand`.

### 6.2 `PluginToolbar` (custom `ItemsControl`)

- Height: 36px (auto when wrapping). Bottom border: 1px `Fo.HairBrush`. Background: `Fo.Ink1Brush`.
- Internal items panel: `WrapPanel` (horizontal). At narrow widths children wrap onto a second 36px row.
- Recognized children: `Button`, `ToggleButton`, `Separator`, `fo:ToolbarSpacer`, `ProgressBar`, `CheckBox`, `TextBlock`.
- Implicit toolbar `Button` style: 28px high, no border, hover background `Fo.ControlHoverBrush`, pressed background `Fo.ControlPressedBrush`. Disabled opacity 0.55.
- `ToolbarSpacer` is a zero-content `FrameworkElement` with `HorizontalAlignment=Stretch` inside a flexible layout so subsequent siblings dock right.
- Plugins replace their current `<Grid Grid.Row="0">…<WrapPanel>` opener with `<fo:PluginToolbar>…</fo:PluginToolbar>`.

### 6.3 `StatusPip`

- Visual: 6px circle + optional caption label. States via `State` enum DP: `Idle`, `Busy`, `Ok`, `Warning`, `Error`.
- `Busy` animates: 1s pulse on opacity 0.4 ↔ 1.0 in amber.
- Used in the status bar (global busy) and conceptually available to plugin authors later.

### 6.4 Refreshed status bar

Layout (left → right):

```
| {plugin name} | {pip} {profile name} | {busy pip} {label} | conn {ago}    [★ update]  [{channel}]
```

- Each segment a `Border` with 1px right hairline and 10px horizontal padding.
- "conn {ago}" updates via a `DispatcherTimer` in `AppShellViewModel`: ticks every 30s, recomputes "just now" / "Nm ago" / "Nh ago" from `LastPingAt`. Hidden when `LastPingAt == null`.
- `★ update` visible only when `HasStagedUpdate`. Clicking it invokes `ApplyUpdateCommand`.
- Channel segment visible only when `ShowUpdaterUi`.

### 6.5 Refreshed title bar

```
| [tX] toolBax v2.0      [● PROD-NZ ▾]          [⋯]   5 plugins
```

- Brand: unchanged.
- `ProfileChip`: docked center-right with 16px left margin from brand.
- `⋯` overflow `Button` opens a `ContextMenu`:
  - `Check updates` — invoke `CheckUpdatesCommand`. Disabled when updater not configured.
  - `Apply update` — `ApplyUpdateCommand`. Disabled unless `HasStagedUpdate`.
  - `Rollback to previous` — `RollbackUpdateCommand`. Disabled unless `HasRollbackUpdate`.
  - Separator.
  - `About toolBax` — opens a small dialog (version, channel, build commit if available).
  - (Future: `Settings…`. Not implemented now; placeholder removed if scope creeps.)
- When `ShowUpdaterUi == false`, the three update menu items are hidden entirely (only About remains).
- `5 plugins` text: unchanged (already bound).

### 6.6 Removed: update bar

The dedicated update bar (`Grid.Row="2"` in current `MainWindow.xaml`) is deleted. Its three buttons live in the overflow menu now; its status text is reflected by the status bar `★ update ready` indicator.

### 6.7 Refreshed `ProfilesView`

```
+-------------+------------------------------------+
| Profiles    | [FO Environment] [CE/Dataverse] [Auth] |
|             |                                    |
| Search [__] | <selected tab content here>        |
|             |                                    |
| > PROD-NZ   |                                    |
|   DEV       |                                    |
|   UAT       |                                    |
|             |                                    |
|             |                                    |
| [+] [Del]   | --- PluginToolbar ----             |
|             | [Refresh][Save][Set active][Test FO][Test CE]
+-------------+------------------------------------+
```

- Left list:
  - Header: "Profiles" + `[+]` add button inline.
  - Search box filters by `Environment.Name` (`CollectionViewSource`).
  - List items: `[●] {Name}` — filled dot when item is the active profile, hollow otherwise.
  - Bottom: `[Delete]` button.
- Right side:
  - `TabControl` with three tabs:
    - **FO Environment** — Name, Base URL, Tenant ID, Default company.
    - **CE / Dataverse** — Base URL, Tenant ID.
    - **Auth** — two columns at ≥1100px, stacks at narrower widths. Each column contains a `Border` card: client ID, auth mode, mode-dependent fields (secret/bearer/cert). Same `AuthMode` visibility triggers as today.
  - Sticky bottom `PluginToolbar` with `Refresh`, `Save`, `Set active`, `Test FO connection`, `Test CE connection`, and right-aligned status text bound to `Status`.

## 7. Data flow

```
Persisted profiles ────► ProfilesViewModel ──┐
                                              │
Connection test result ──► AppShellViewModel ─┤
                                              ▼
                              MainWindowViewModel
                              ├─ Plugins      ──► LeftRail / TabBar
                              ├─ ActiveControl ─► ContentControl
                              ├─ Shell.*      ──► ProfileChip, StatusBar, overflow menu
                              └─ Updater      ──► overflow menu items
```

- `ProfilesViewModel.SaveCommand` writes profile changes; on save it raises `ProfilesChanged`; `AppShellViewModel` subscribes and refreshes its read-only `Profiles` projection.
- `ProfilesViewModel.SetActiveCommand` and `ProfileChip.SetActiveProfileCommand` both call into the existing core `IProfileStore.SetActive(id)`. The shell VM subscribes to the store's `ActiveProfileChanged` event.
- `TestFoConnectionCommand` / `TestCeConnectionCommand` on `ProfilesViewModel` already exist; on completion they publish a `ConnectionTested(scope, isSuccess, timestamp, errorMessage?)` event. `AppShellViewModel` subscribes and updates `ConnectionStatus` + `LastPingAt`.
- `IsBusy` aggregate: when a plugin's `IPluginBusyState.PropertyChanged` fires for `IsBusy`, the shell recomputes `Any(p => p.IsBusy)` and notifies bindings.

## 8. Error handling & edge cases

- **No active profile (fresh install):** chip shows `[○] No profile ▾`, popup shows a single "Set up a profile…" row.
- **Updater not configured:** overflow menu hides the three update items.
- **Plugin manifest references an unknown icon key:** `IconResourceResolver` falls through to name heuristic, then default. Never throws.
- **PluginToolbar overflow at narrow widths:** wraps onto a second 36px row (still scannable). Tested against `DualWriteMapBrowser` (7 buttons + checkbox + progress).
- **Tab switch in `ProfilesView` with unsaved changes:** binding is two-way to the in-memory profile object; switching tabs preserves edits. Persistence is explicit (Save button).
- **`ConnectionTested` for a profile that is not currently active:** shell ignores it (only the active-profile ping populates the status bar).
- **`Fluent.Light.xaml` rename:** `App.xaml` is the only reference; updated atomically in the same commit. If anyone has a fork referencing the old name, the build fails loudly — that's the right outcome.
- **`ProfileChip` with very long profile name:** truncates with ellipsis at 160px max width; full name in tooltip.

## 9. Backwards compatibility

- Existing plugin XAML compiles unchanged (it only consumes `DynamicResource` keys, all of which remain).
- Plugins do **not** have to adopt `PluginToolbar`; the refresh migrates the four shipped plugins in this PR. Third-party plugins continue to work with their own toolbars.
- Plugins do **not** have to implement `IPluginBusyState`; the shell treats absent implementations as idle.
- Plugin manifest `icon` field is optional; absent manifests fall back to the name heuristic (today's behaviour).
- The legacy brush aliases (`Fo.SurfaceBrush`, `Fo.BorderBrush`, etc.) are preserved.

## 10. Testing & verification

### 10.1 Static
- `dotnet build .\FoToolbox.sln -c Release` — XAML compilation catches resource-key typos and broken bindings.

### 10.2 Automated
- `dotnet test .\FoToolbox.sln -c Release` — existing tests must still pass. Watch in particular:
  - Plugin discovery layout-matrix tests (added in T20).
  - Profile persistence tests.
- New unit tests live in `tests/FoToolbox.Tests` (the existing test project; no new project needed):
  - `IconResourceResolver_ExplicitKey_ResolvesToGeometry`
  - `IconResourceResolver_UnknownKey_FallsBackToNameHeuristic`
  - `IconResourceResolver_NoMatch_ReturnsDefaultPluginIcon`
  - `AppShellViewModel_IsBusy_TrueWhenAnyPluginBusy`
  - `AppShellViewModel_IsBusy_FalseWhenAllPluginsIdle`
  - `AppShellViewModel_ConnectionTested_UpdatesStatusAndTimestamp`
  - `AppShellViewModel_NoActiveProfile_EmitsNullStateForChipBindings`
  - `ProfileChip_NavigateToProfilesRequested_FiresOnRightClick`

### 10.3 Manual smoke
1. Launch host. Title bar shows profile chip; status bar shows plugin + profile + idle pip.
2. Click chip → popup lists profiles → pick a different one → status bar profile name updates, dot stays "Unknown" until a Test runs.
3. Click ⋯ overflow → menu items reflect updater configuration state.
4. Open Profiles tab. List shows active profile marked. Click `Test FO connection`. After success: status bar pip turns green, `conn just now` appears.
5. Edit a profile field on the FO tab, switch to Auth tab, switch back — edit survives.
6. Trigger an update env-var, click `Check updates` in overflow, observe status bar `★ update ready`, click it → installer launches.
7. Resize window to 1280x820 (min). PluginToolbar wraps cleanly; ProfilesView Auth tab stacks instead of side-by-side.
8. Tab through controls with `Tab` key — focus rings render with `Fo.AccentBrush`.

## 11. Migration plan

The implementation plan (next step, via writing-plans skill) will break this into:

1. **Foundations:** add `Spacing.xaml`, `Icons.xaml`, rename `Fluent.Light.xaml` → `Fluent.Theme.xaml`. Build green.
2. **`IconResourceResolver` + manifest field:** wire icon resolution through; remove `IconPathFor` from VM. All four plugins still work via name-heuristic fallback. Build + tests green.
3. **`AppShellViewModel`:** introduce, wire to `MainWindowViewModel` as composed property. Bind to a stub in MainWindow first; no UI change yet. Tests green.
4. **`PluginToolbar` + `Fo.Toolbar.Button` style:** ship the control. No plugin uses it yet.
5. **`ProfileChip`:** ship the control. Wire into `MainWindow.xaml` title bar. Visible improvement #1.
6. **Refreshed status bar:** new layout, `StatusPip`, "conn ago" timer. Visible improvement #2.
7. **Overflow menu + delete update bar:** rewire updater commands. Visible improvement #3.
8. **`ProfilesView` rewrite + `ProfilesViewModel`:** the largest single change. Tested in isolation. Visible improvement #4.
9. **Plugin migrations:** each of QueryBuilder, TableEntityBrowser, DualWriteMapBrowser, ODataPostBuilder adopts `PluginToolbar`, drops 6px CornerRadius, opts in to `IPluginBusyState`. One commit per plugin so reverts are surgical.
10. **Plugin manifest icon updates:** each plugin's manifest gains its `"icon"` field. Cosmetic; safe last step.

Each step is independently buildable, testable, and shippable. The order ensures the build is never broken by halfway state.

## 12. Open questions

- The "About toolBax" dialog content (version + commit). Defer to implementation; pull from `AssemblyInfo`.
- Plugin manifest schema lives in `FoToolbox.Host/Plugins/PluginManifestReader.cs`. Confirm the manifest file format (JSON vs other) during implementation and update `PluginManifestReader` and the four plugin manifests accordingly.
- `ToolbarSpacer` semantics inside a `WrapPanel`: a wrap panel does not naturally honor "stretch to fill remaining space". If a right-aligned ProgressBar matters more than wrap behaviour, the internal panel may need to fall back to a `DockPanel` with the spacer set to `LastChildFill="False"`. Decide during implementation; prefer the wrap behaviour and accept that right-alignment is approximate.

## 13. Out of scope (deferred)

These are good ideas that are not in this refresh; capture for later:

- Light theme.
- Ctrl+K command palette.
- Notification drawer / toast host.
- Real Settings screen (the overflow `Settings…` placeholder is intentionally absent).
- Drag-to-reorder tabs, close-button on tabs.
- Per-plugin theming (plugin-supplied accent).
