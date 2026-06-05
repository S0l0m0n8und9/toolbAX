# Design tokens → Avalonia resources

All values are final (high-fidelity). Source of truth: `prototype/avalonia.css`. Implement as a
merged `ResourceDictionary` (`Themes/tokens.axaml`) plus a `FluentTheme` accent override. Use
`FluentTheme` **dark** as the base.

## Color palette

### Surfaces (Mica-layered neutrals, dark)
| Token | Hex | Use |
|---|---|---|
| `AppBackground` | `#1D1D20` | window background |
| `Mica` | `#222226` | caption bar, nav pane, status strip, sticky toolbars, grid header |
| `Layer1` | `#272729` | cards, content panes, dialogs |
| `Layer2` | `#2D2D31` | control fill (rest): buttons, inputs, combo |
| `Layer3` | `#36363B` | control fill (hover); selected nav item |
| `Layer4` | `#404048` | pressed / chip |

### Strokes & dividers (white over dark, use opacity)
| Token | Value |
|---|---|
| `Stroke` | `#FFFFFF` @ 8.5% |
| `Stroke2` | `#FFFFFF` @ 14% |
| `Divider` | `#FFFFFF` @ 6% |
| `SubtleHover` | `#FFFFFF` @ 7% (list/row hover) |

### Text ramp
| Token | Value | Use |
|---|---|---|
| `Text0` | `#FFFFFF` @ 92% | primary |
| `Text1` | `#FFFFFF` @ 78% | body / grid cells |
| `Text2` | `#FFFFFF` @ 55% | labels, captions |
| `Text3` | `#FFFFFF` @ 38% | hints, disabled-ish |

### Accent (brand amber — set as the Fluent `AccentColor`)
| Token | Hex | Use |
|---|---|---|
| `Accent` | `#E3A83F` | primary accent: selection bar, primary buttons, active tab |
| `AccentHover` | `#EAB863` | accent hover (lighter, per Fluent) |
| `AccentPressed` | `#C8902F` | accent pressed |
| `OnAccent` | `#1A1611` | text/icon on accent fill (near-black) |
| `AccentTint` | `#E3A83F` @ 14% | selected row background |
| `AccentTint2` | `#E3A83F` @ 22% | selected row hover |

> In Avalonia, set `FluentTheme` accent via the `SystemAccentColor` resource (and the
> `SystemAccentColorLight1/2/3`, `Dark1/2/3` variants) so native controls pick up amber. Map
> `Accent`→`SystemAccentColor`, `AccentHover`→`SystemAccentColorLight1`, etc.

### Status palette (badge fg + 12%-alpha bg of same hue)
| Token | Hex | Bg |
|---|---|---|
| `Ok` | `#5FCE8E` | @12% |
| `Warn` | `#E9C45A` | @12% |
| `Err` | `#F2655E` | @12% |
| `Info` | `#5BB4F2` | @12% |

State→color mapping for map status: running→Ok, paused→Warn, errored→Err, stopped/idle→neutral
(Text2). Transitional verbs (starting/pausing/…)→Accent with a pulsing dot.

## Typography

- **UI font:** `Segoe UI Variable Text` → fall back `Segoe UI Variable`, `Segoe UI`, system.
- **Display font** (screen `<h1>` titles): `Segoe UI Variable Display`, same fallbacks, weight 600.
- **Monospace:** `Cascadia Code` → `Cascadia Mono`, `Consolas`. Used for all identifiers, URLs,
  GUIDs, table/field names, log lines, gateway hosts. Enable tabular figures.

| Role | Size | Weight | Notes |
|---|---|---|---|
| Screen title (h1) | 24–28px | 600 | Display font; 24 inside tools, 28 on Plugins home |
| Card header | 13.5–14px | 600 | |
| Body | 13–13.5px | 400 | |
| Grid cell | 13px | 400 | mono 12–12.5px for identifiers |
| Label / caption | 11–12.5px | 400 | Text2/Text3 |
| Section eyebrow | 10–11px | 400 | letter-spacing 0.08em, uppercase, Text3 |
| Kbd hint | 11px | 400 | mono |

## Shape & spacing

| Token | Value | Use |
|---|---|---|
| `CornerRadiusControl` | `4` | buttons, inputs, combo, checkbox, nav item |
| `CornerRadiusCard` | `8` | cards, dialogs, infobar, command palette |
| `CornerRadiusPill` | `999` | badges, toggle track |

Control heights: button **32** (small 28), input **32**, combo **32**, grid row **40**, grid
header **38**, nav item **38**, status strip **26**, caption bar **40**, sticky toolbar **48**.

Page padding: tool content **16–24px** horizontal; cards **16px** inner; card header **11×16px**.
Gaps: button rows **8px**; form field label column **130px** fixed, **14px** gap to control.

## Control-specific notes

- **Buttons:** rest = `Layer2` fill + `Stroke` border; hover = `Layer3`; primary/accent = `Accent`
  fill + `OnAccent` text + weight 600; subtle = transparent until hover (`SubtleHover`); danger =
  `Err` text + 40%-alpha err border, hover `Err`@12% fill; destructive-primary = `Err` fill.
- **Inputs:** `Layer2` fill, bottom border `Stroke2`; on focus the bottom border becomes 2px `Accent`
  (Fluent's underline-grows behavior — `TextBox` already does this; just theme the brushes).
- **DataGrid:** header `Mica` bg + `Stroke` bottom border, Text2 600; rows 40px, `Divider` bottom
  border; hover `SubtleHover`; **selected row = `AccentTint` bg + 3px inset `Accent` left bar**
  (selection bar is the key brand cue — replicate via a cell/row template left border).
- **InfoBar:** use Fluent `InfoBar`. Warning severity = `Warn` icon + `Warn`@12% bg + 32%-alpha
  border. The Operations live banner is `Severity=Warning`, `IsClosable=False`.
- **ToggleSwitch / segmented:** the prototype uses a 2-option segmented control (amber selected
  segment). Realize as a styled `ToggleButton` group or `TabStrip`; selected = `Accent`/`OnAccent`.
- **Badge / status pill:** small templated control `StatusBadge` — pill, 22px tall, fg = status
  color, bg = status@12%, optional 7px leading dot in `currentColor`.
- **Scrollbars:** Fluent thin overlay scrollbars are fine; the prototype uses a thin translucent
  thumb (`#FFFFFF`@18%, hover 30%).

## Iconography

The prototype uses a small inline stroke-SVG set (`prototype/components/ui.jsx`, `Icon`): plug,
database, bolt, map, branch, book, terminal, key, search, refresh, play, pause, stop, check, alert,
user, plus, save, download, arrow-r/lr, chev-d, menu, logs, sparkles. Use a single icon source in
the app (e.g. a `PathIcon`/`Geometry` resource dictionary, or `Projektanker.Icons.Avalonia` /
Fluent System Icons). Match the **semantic mapping**, not the exact paths. Stroke ~1.6–2px,
`currentColor`, sizes 13–18px per context.
