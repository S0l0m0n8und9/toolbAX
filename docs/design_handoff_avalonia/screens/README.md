# Screens — visual reference

High-fidelity captures of the Avalonia-Fluent prototype at ~1480px desktop width. These are the
**target renderings** for each screen. Build against these + `../control-map.md`. (Captured from
`../prototype/toolBax-avalonia.html` — open it to click through live.)

| # | File | Screen | Notes |
|---|---|---|---|
| 0 | `00-shell.md` | — | Shell anatomy callouts (see below) |
| 1 | `01-plugins-home.png` | Plugins home | Card launcher; "operates live" badge on Operations; signed-check per card |
| 2 | `02-query-builder.png` | Query Builder | Entity list · field chips · OData URL · results DataGrid · result status bar |
| 3 | `03-operations.png` | **Dual-Write Operations** | Live InfoBar · gateway row · CommandBar w/ eligibility counts · maps DataGrid · request log |
| 3b | `03b-operations-confirm.png` | Operations — confirm | ContentDialog: "Stop 3 maps?", `action=4`, destructive caveat, target list, red Stop |
| 4 | `04-map-browser.png` | **Dual-Write Map Browser** | Read-only inspector: master list · KPIs · sparkline · Bindings/Value maps/Runs/Errors tabs |
| 5 | `05-compare.png` | Dual-Write Compare | Source→target combos · diff summary chips · two-level-header diff grid |
| 6 | `06-metadata.png` | Metadata Browser | Entity-set list · property table (type/key/nullable) |
| 7 | `07-post-builder.png` | POST Builder | Method+path · request body / response split |
| 8 | `08-profiles.png` | Profiles | Master list w/ active marker · pivot tabs (FO/CE/Auth/Data Integrator) · sticky toolbar |

## Shell anatomy (all screens share this chrome)

- **Caption bar (40px, Mica):** app glyph + "toolBax — {env}", centered Ctrl+K search/command
  entry, Windows min/max/close buttons. Drag region via `ExtendClientAreaToDecorationsHint`.
- **NavigationView (248px, collapsible to 52px):** hamburger, section headers (TOOLS / SYSTEM),
  8 nav items, "live" badge on Operations, **active item = Layer3 bg + 3px inset amber bar**,
  docked profile switcher footer (initials chip + name + status dot).
- **Status strip (26px, Mica):** active tool · env (dot + legal + name) · busy (idle/working…) ·
  conn-ago · [right] branch · "SDK 1.2.0 · .NET 10" · "update ready" (amber).

## Reading the captures

- Brand amber appears **only** as the accent (selection bar, primary buttons, active tab, links).
  Everything else is neutral Mica grey — that's `FluentTheme` dark + a single `AccentColor` override.
- Mono font (Cascadia) is used for all identifiers: entity/field/table names, URLs, GUIDs, hosts,
  log lines, versions, gateway codes.
- Status colors: running→green, paused→amber, errored→red, stopped/idle→neutral. Transitional
  verbs (pausing…) render as amber text + a pulsing dot.
- Window is shown ~924px wide in some captures because the preview pane clips the right edge — the
  app design width is ~1280+; layout reflows, nothing is fixed-width-broken.
