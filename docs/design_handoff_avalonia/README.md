# Handoff: toolBax — Avalonia 12 implementation

> **Historical document.** This is the design-era handoff package that was written *before* the Avalonia app
> was built, and it is kept as a record of the intended design. It is not a description of current behaviour —
> the shipped code is authoritative wherever the two disagree.

## Overview

**toolBax** (a.k.a. FO Toolbox 2.0) is a Windows-first desktop toolbox for Dynamics 365
Finance & Operations developers/implementers — an XrmToolBox-style plugin host. This package
hands off the **UI + interaction design** so it can be built as a real **Avalonia 12 (.NET 10)**
MVVM application, with **headless UI testing** as a first-class requirement.

The flagship tool is **Dual-Write Operations**: it drives the Dual-Write Management gateway
(start / stop / pause / resume / initial-sync over selected entity maps) with confirm-on-mutation
and live status polling. The rest of the suite is read/compose tooling (Query Builder, Metadata
Browser, POST Builder, Dual-Write Map Browser + Compare) plus Profiles (environments, auth,
Data Integrator credentials).

## About the design files

The files under `prototype/` are a **design reference built in HTML/React** — a high-fidelity
prototype showing intended look, layout, and behavior. **They are not production code to copy.**
Your task is to **recreate this design natively in Avalonia 12 using XAML + MVVM** and the
patterns/contracts described here. The HTML is the pixel/interaction source of truth; the
Markdown docs are the translation layer to Avalonia.

Open `prototype/toolBax-avalonia.html` in a browser to see and click the target. (It already
mimics Avalonia's FluentTheme — neutral Mica greys, Segoe UI Variable, 4–8px radii, brand amber
as the single accent — so what you see is close to what the native app should look like.)

## Fidelity

**High-fidelity.** Colors, typography, spacing, control idioms, and interactions are final.
Recreate pixel-faithfully using Avalonia's `FluentTheme` + the accent/token overrides in
`design-tokens.md`. Where the prototype fakes a native control with HTML (e.g. a `<table>` for
the DataGrid, a styled popup for the command palette), the doc names the **real Avalonia control**
to use instead — follow the doc, not the HTML implementation detail.

## How to use this package (recommended order)

1. Read **`CLAUDE.md`** — stack, project layout, hard constraints. Keep it in context.
2. Scaffold the solution and get the **headless test harness green on an empty view**
   (`headless-testing.md`) *before* building screens. The testing requirement is the reason
   Avalonia was chosen over WPF/WinUI — prove it works first.
3. Apply **`design-tokens.md`** as a `ResourceDictionary` + `FluentTheme` accent override.
4. Build the **shell** (NavigationView, caption bar, status strip, command palette) from
   `control-map.md` §0.
5. Build the **vertical slice first**: **Dual-Write Operations** (§3) and **Profiles** (§8).
   These carry the real behavior and risk (gateway mutations, auth, polling). Wire them against
   the service interfaces in `viewmodels-and-services.md` with fakes, and write the headless
   tests as you go.
6. Fan out to the remaining read/compose screens (§1, §2, §5, §6, §7), which are simpler
   `DataGrid`/form views.

## Scope of this package

- **Full depth** (control map + ViewModel contract + sample headless test): Dual-Write Operations, Profiles.
- **Control map** (element → control → binding): Shell, Plugins home, Query Builder, Dual-Write
  Compare, Metadata Browser, POST Builder, **Dual-Write Map Browser** (read-only inspector — now
  drawn in full; see `screens/04-map-browser.png` and `control-map.md` §4).
- **Visual reference**: `screens/` has a high-fidelity capture of every screen + the Operations
  confirm dialog, with a shell-anatomy index (`screens/README.md`).

## Files in this package

| File | What it is |
|---|---|
| `README.md` | This file — orientation + build order |
| `CLAUDE.md` | Constraints/brief for Claude Code to keep in context |
| `design-tokens.md` | Palette, type, radii, spacing → Avalonia resources |
| `control-map.md` | Screen-by-screen: prototype element → Avalonia control → binding → VM boundary |
| `viewmodels-and-services.md` | ViewModel contracts + service interface stubs (the testable seams) |
| `headless-testing.md` | `Avalonia.Headless.XUnit` harness setup + a sample Operations test |
| `screens/` | High-fidelity capture of every screen + confirm dialog + shell-anatomy index |
| `prototype/` | The HTML/React design reference (open `toolBax-avalonia.html`) |

## A note on what's verified

This package was authored *for* Claude Code to execute; the .NET code in it (interface stubs,
the sample test) is a **high-fidelity spec, not compiled output**. Treat the first build step —
scaffold + green headless harness — as the moment you confirm versions/APIs against the live
NuGet packages, then build against this spec. The repo's existing F&O service code
(OData client, MSAL auth, gateway client, DPAPI profile store) is the integration target;
wire ViewModels to it through the interfaces in `viewmodels-and-services.md`.
