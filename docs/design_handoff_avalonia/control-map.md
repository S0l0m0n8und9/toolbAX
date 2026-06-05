# Control map

Per screen: prototype element → **Avalonia control** → key bindings/commands → **ViewModel boundary**.
"VM" = the property/command on the screen's ViewModel (contracts in `viewmodels-and-services.md`).
Layout numbers are from the prototype; treat as targets, round to the spacing scale.

Window root: a `Window` with custom chrome — `ExtendClientAreaToDecorationsHint=True`,
`ExtendClientAreaChromeHints=NoChrome`, custom caption bar (§0.1). Root layout is a vertical stack:
**Caption bar (40) / [NavView | content] / Status strip (26)**.

---

## §0 Shell  (`prototype/components/av-shell.jsx`)

### 0.1 Caption bar — `Border` (Mica), `Grid` 3-col, `PointerPressed`→`BeginMoveDrag`
| Element | Control | Binding / behavior |
|---|---|---|
| App glyph + "toolBax" + "— {env}" | `StackPanel` H, `TextBlock` | env name ← `Shell.ActiveEnvironment.Name` |
| Center "Search tools & run commands" pill + `Ctrl+K` | `Button` styled as search box | Click → `OpenCommandPaletteCommand` |
| Min / Max / Close | 3 × caption `Button` (46px) | `WindowState`/`Close`; Close hover = `#C42B1C` |

The caption bar is the drag region (`ExtendClientAreaToDecorationsHint`); interactive children set
their own hit-testing. Window control buttons live on the right.

### 0.2 NavigationView — Fluent `SplitView` or `NavigationView`-style pane (Mica, 248px / 52px collapsed)
| Element | Control | Binding |
|---|---|---|
| Hamburger toggle | `Button` (subtle, icon) | toggles `IsPaneOpen` |
| Section headers ("TOOLS"/"SYSTEM") | `TextBlock` eyebrow | static; render as divider when collapsed |
| Nav items (8) | `ListBox` of nav items, or `RadioButton` group | `SelectedItem` ↔ `Shell.CurrentTool`; Alt+letter accelerators |
| "live" badge on Operations item | `StatusBadge` (warn) | static flag on the nav model |
| Footer: profile switcher | `Button` + `Flyout`/`Popup` | see 0.3 |

Selected nav item = `Layer3` bg + **3px inset `Accent` left bar** + Text0. Items: Plugins(home),
Query(Q), Dual-Write Operations(O), Dual-Write Map Browser(D), Dual-Write Compare(C),
Metadata(M), POST(P), Profiles(E). Navigation is a `ContentControl` in the content area whose
`Content`/`DataTemplate` switches on `Shell.CurrentTool` — use a `DataTemplates` map of
ToolVM→View, not manual visibility toggles.

### 0.3 Profile switcher (nav footer) — `Button` opens `Flyout`
- Button shows env initials chip + name + status dot + chevron. ← `Shell.ActiveEnvironment`.
- Flyout: `ListBox` of `Shell.Environments`; selecting → `SetActiveEnvironmentCommand(env)`.
- Status dot color ← env.Status (connected→Ok, token-expired→Warn, disconnected→Err).

### 0.4 Status strip — `Border` (Mica, 26px), `DockPanel`/`Grid` of segments
Segments (each `Stroke`-divided): active tool label · env (dot + legal + name) · busy
(`idle`/`working…` with pulsing accent dot) · "conn {ago}" · [right] branch · "SDK 1.2.0 · .NET 10"
· "update ready" (accent). Bindings: tool label ← `Shell.CurrentTool.Title`; busy ←
`Shell.IsBusy` (aggregate of any tool's busy state); conn ← `ActiveEnvironment.LastConnected`.

### 0.5 Command palette — overlay `Panel` + centered `Border` (Layer1, card radius)
Realize as a `Popup`/`Flyout` or an overlay in an `OverlayLayer` (do **not** spin a second
`Window`). `Ctrl+K` toggles `Shell.IsCommandPaletteOpen`; `Esc` closes.
| Element | Control | Binding |
|---|---|---|
| Search field (autofocus) | `TextBox` | `Palette.Query` |
| Results | `ListBox` | `Palette.FilteredCommands` (filter on Query); Enter/click → `InvokeCommand(item)` |
Each command = label + kbd hint; invoking a "tool" command sets `CurrentTool`.

---

## §1 Plugins home  (`av-home.jsx`) — VM: `PluginsHomeViewModel`
- Header: `TextBlock` display 28 "Plugins" + subtitle (env name accent, `Ctrl+K` kbd) + a 240px
  search `TextBox` (top-right) ← `Filter`.
- Card grid: `ItemsControl` with a `UniformGrid`/`WrapPanel` (min col 300px, 14px gap) over
  `FilteredPlugins`. Each card = `Button` (whole card clickable → `OpenPluginCommand(id)`):
  - icon chip (accent-tint bg if `Hot`), name (600), `v{version} · {cat}` mono Text3,
    signed check (`Ok`) / unsigned alert (`Warn`) top-right.
  - description `TextBlock` (Text2, wrap).
  - footer: `Alt+{shortcut}` kbd, "operates live" badge if `Live`, "built-in" if `Builtin`, arrow.
- Data ← `IPluginCatalog.Plugins` (shape = `data.js` `window.PLUGINS`).

---

## §2 Query Builder  (`av-extra.jsx` AvQuery) — VM: `QueryBuilderViewModel`
2-col grid: 260px entity list (Mica) | content.
| Element | Control | Binding |
|---|---|---|
| Entity list | `ListBox` mono items (name + field count) | `Entities` / `SelectedEntity` |
| Title row | `TextBlock` mono h1 + "company-aware" badge + `pk:` caption | from `SelectedEntity` meta |
| Query URL | read-only `TextBox`/`TextBlock` mono, flex | computed `QueryUrl` (from SelectedFields) |
| Run / CSV | `Button` accent / `Button` | `RunCommand` / `ExportCsvCommand` |
| Field chips | `ItemsControl` of toggle chips | `Fields`; toggle → updates `SelectedFields` set; PK marker |
| Results grid | `DataGrid` | `ResultRows` (cols = selected fields); right-align numerics |
| Result status bar (28px) | `Border` + badges | `RowCount`, latency, `200 OK` badge |
Entity/field/row data ← `IMetadataService` + `IODataClient` (prototype: `window.ENTITIES`,
`window.FIELDS[name]`, `window.SAMPLE_ROWS`). If a field list isn't cached, show the
"run once to populate" empty/info state.

---

## §3 Dual-Write Operations  ★ flagship  (`av-ops.jsx`) — VM: `DualWriteOpsViewModel`
See `viewmodels-and-services.md` §A for the full VM contract and `headless-testing.md` for tests.

Vertical layout inside the screen:
1. **Title** `TextBlock` display 24 "Dual-Write Operations".
2. **Live banner** — Fluent `InfoBar`, `Severity=Warning`, `IsClosable=False`, `IsOpen=True`
   (permanent). Message names the env: `ActiveEnvironment.GatewayCName`. Bold "Live environment."
3. **Gateway connection row** — `WrapPanel` of meta pairs (label/value): gateway region, identifier,
   cid; right side: auth `StatusBadge` (`{mode} · {account}`), token-expiry caption, "Discover"
   subtle button (`DiscoverHostCommand`). ← `Gateway` (shape = `window.DW_GATEWAY`).
4. **CommandBar** — `StackPanel`/`WrapPanel` H of action buttons, one per `window.DW_ACTIONS`:
   | Action | Code | Enabled when a selected map is in… | Button style |
   |---|---|---|---|
   | Start | 1 | stopped, idle | normal |
   | Stop | 4 | running, paused | **danger** |
   | Pause | 5 | running | normal |
   | Resume | 6 | paused | normal |
   | Initial sync | 8 | any | **danger** |
   Each button: `Command=RunActionCommand`, `CommandParameter=action`; `IsEnabled` ←
   `!IsBusy && EligibleCount(action) > 0`; label shows `· {EligibleCount}`. After the bar:
   "{SelectedCount} selected"; right: live "polling {requestId} · {done}/{total}" when busy.
5. **Maps DataGrid** — `Avalonia.Controls.DataGrid`, `ItemsSource=Maps`, multi-select OR a
   leading checkbox column bound to each row VM's `IsChecked` (prototype uses a checkbox column +
   header tri-state select-all). Columns: ☑ | Table map (`{fo} {dirArrow} {dv}`, mono) | Flow |
   Template (`v{tmplVersion}`) | Author (Info color if not "Microsoft") | Rows 24h (right) |
   Errors (right, Err if >0) | State. **State cell:** if transitional → accent text + pulsing dot
   `"{verb}…"`; else `StatusBadge`. Selected row = AccentTint + inset accent bar. Row VM =
   `MapRowViewModel` (see §A). Direction arrows: both ↔, fo→dv →, dv→fo ←.
6. **Gateway request log** — bottom `Border` (Mica, ~140px max): header "Gateway requests" +
   `{done}/{total}` when busy; `ItemsControl`/`ListBox` over `Log` (ts mono · status dot · text
   mono · note mono Text3). ← `Log` collection appended by the VM.

**Confirm dialog** — Fluent `ContentDialog` (`ConfirmActionViewModel`), opened by `RunActionCommand`
before any gateway call:
- Title: "{Action} {N} map(s)?"
- Body: "Sends `action={code}` to the Dual-Write gateway for {cname}." + red caveat line for
  Stop ("halts replication…") and Initial-sync ("re-syncs all data… can run a long time").
- A bordered list of the target maps (each: `{fo} {arrow} {dv}` + current `StatusBadge`).
- Footer: Cancel / **{Action}** (accent; danger-accent = `Err` fill for Stop & Initial-sync).
- Confirm → `ExecuteActionCommand` actually calls the gateway + starts polling. Cancel → close.

**Behavior to preserve (also see CLAUDE.md):** on confirm, set targeted maps to the verb state,
POST the action, then poll Status until terminal, settling each map to the result state
(start/resume/initial→running, stop→stopped, pause→paused) and appending log lines. Eligibility is
recomputed from the *current* states of *checked* maps, so already-paused maps are excluded from a
Pause, etc.

---

## §5 Dual-Write Compare  (`av-extra.jsx` AvCompare) — VM: `DualWriteCompareViewModel`
- Header: title + **source `ComboBox`** → arrow → **target `ComboBox`** (both over `Environments`)
  + accent "Compare" button (`IsEnabled` when source≠target) + caption.
- If source==target → empty state ("Pick two different environments").
- Summary chips: `ItemsControl` of `StatusBadge` per diff bucket with counts (in sync / version
  drift / state differs / row delta / only in source / only in target).
- **Diff DataGrid** with a **two-level header**: a top group row spanning `{srcLegal} source` (3
  cols) and `{tgtLegal} target` (3 cols), then sub-columns state/template/rows under each.
  `Avalonia.DataGrid` has no native column grouping — realize the group header as a separate
  `Grid` row aligned above the DataGrid, or a custom header. Columns: Table map | (src) state /
  template / rows | (tgt) state / template / rows | Diff badge. Absent side → "absent" italic cell.
  Version mismatch → target template in `Warn`. Rows ← `DiffRows` from `IDualWriteCompareService`.

---

## §6 Metadata Browser  (`av-extra.jsx` AvMetadata) — VM: `MetadataViewModel`
- 2-col: 300px entity-set `ListBox` (name + "{count} props · {module}") | detail.
- Detail header: eyebrow "ENTITY · {module}", mono h1 name, "company-aware" badge, `pk:` + prop
  count caption.
- Property table: `DataGrid` over `Fields` — Property (mono) | Type (`Enum<…>`/`String(len)`/…) |
  Key (`key` badge) | Nullable. If fields not cached → Fluent `InfoBar` (Info) "open in Query
  Builder to fetch $metadata". Data ← `IMetadataService` (`window.ENTITIES` + `window.FIELDS`).

---

## §7 POST Builder  (`av-extra.jsx` AvPost) — VM: `PostBuilderViewModel`
- Header: method `ComboBox` (POST/PATCH/DELETE) + path `TextBox` (mono, flex) + accent "Send".
- Body split 1:1: left request `TextEditor`/`TextBox` (mono, multiline) ← `RequestBody`; right
  response: header with status badge (`201 Created · {ms}`) + `pre` response viewer ← `ResponseBody`.
- Send → `SendCommand` via `IODataClient`. (For a richer editor, `AvaloniaEdit` is optional.)

---

## §4 Dual-Write Map Browser  (`av-mapbrowser.jsx`) — VM: `DualWriteMapViewModel`
**Read-only** inspector — no CommandBar, no mutations (that's Operations). Master/detail:
- **Master** (320px, Mica): search `TextBox` + `ListBox` over `Maps`; each item = status dot +
  `{fo} {arrow} {dv}` (mono) + `v{version} · {lastRun}` + error count. ← `IDualWriteMapService`.
- **Detail header:** a permanent read-only `InfoBar` (Info: "open Operations to act"); eyebrow +
  state/direction/version badges; big mono `{fo} {arrow} {dv}` title; KPI row (rows 24h, errors 24h,
  latency p95) + a 24h activity **sparkline** (`Polyline` in a small `Canvas`/custom control).
- **Pivot tabs:** Bindings · Value maps · Runs · Errors (Errors tab shows a red dot when errors>0).
  | Tab | Control | Content |
  |---|---|---|
  | Bindings | `DataGrid` | # · {fo} field (PK badge) · flow arrow (or "skip") · {dv} field · transform · flags. ← `map.Bindings` |
  | Value maps | `ItemsControl` of `fl-card` each w/ a small `DataGrid` | F&O value → Dataverse value rows; "+N more" footer. ← `map.ValueMaps` |
  | Runs | `DataGrid` | time · trigger (initial-sync badge) · rows · ok · failed · duration · result badge. ← `IDualWriteMapService.GetRunsAsync` |
  | Errors | `ItemsControl` of error cards | severity icon · message · `ts · code · key · field` mono line · Retry button. ← `GetErrorsAsync` |
  Empty/uncached states use Fluent `InfoBar`/empty placeholders (bindings not cached, no value maps,
  no errors in 24h). Data shapes: `window.DW_MAPS` (full detail on `cust-account`); Runs/Errors are
  illustrative in the prototype — back them with real run-history/error endpoints.

> See `screens/04-map-browser.png` for the target rendering. Confirm Runs/Errors data sources with
> the backend (run-history + dead-letter/error tables) before wiring.
