# toolBax — Avalonia host (alternate UI spike, #35)

A from-scratch **Avalonia 12 / .NET 10 MVVM** rebuild of the toolBax UI, kept separate from the
default WPF host (`src/FoToolbox.Host`). It exists because the design handoff in
`docs/design_handoff_avalonia/` chose Avalonia for one reason above all: **headless UI tests that run
in CI with no display server** (impossible with WPF/WinUI). The decoupled, WPF-free plugin contract
(`FoToolbox.SDK`, #33) is what makes an alternate host viable.

## Layout

| Project | TFM | What |
|---|---|---|
| `toolBax.App` | `net10.0` | Avalonia UI — Views (`.axaml`) + ViewModels (`CommunityToolkit.Mvvm`), FluentTheme dark + `Themes/Tokens.axaml` (brand amber accent). |
| `toolBax.App.Tests` | `net10.0` | `Avalonia.Headless.XUnit` tests (xunit **v3**). |

Solution: `toolBax.slnx`. Package versions are centrally managed in the repo's `Directory.Packages.props`.

## Build & test

```powershell
dotnet build .\avalonia\toolBax.slnx -c Release
dotnet test  .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release
```

The headless tests run on a plain runner with **no display** — see the `avalonia-tests` CI job. This
is the de-risking milestone the handoff insists on proving before screens are built.

## Status (Phase 1)

Scaffold + green headless harness + design tokens + a minimal shell window. Next: the shell
(nav rail, command palette, status strip) per `docs/design_handoff_avalonia/control-map.md` §0, then
the Dual-Write Operations + Profiles vertical slice. The WPF host stays the default meanwhile.
