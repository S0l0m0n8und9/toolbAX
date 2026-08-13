# FO Toolbox

FO Toolbox (toolbAX) is a desktop toolbox for Dynamics 365 Finance & Operations (F&O) and Dataverse,
inspired by XrmToolBox-style workflows. It's an [Avalonia](https://avaloniaui.net/) app over a shared
.NET core library.

It provides:
- Environment/profile management, including a header switcher for the active environment
- Entra ID auth (MSAL interactive, client secret, certificate)
- OData metadata exploration and query tools, with cancellable runs
- A POST / write (OData) builder with metadata-backed payload validation
- Dual-write map browser, operations, and compare tooling — row-count checks report capped or
  not-comparable counts rather than a false Match/Mismatch
- A Dataverse virtual-tables inspector for the F&O-backed tables
- CSV export

## Status

This repository is in active development. APIs and behavior may change.

## Download

Releases are published as GitHub Releases:

**[→ Latest release](https://github.com/S0l0m0n8und9/toolbAX/releases/latest)** · **[All releases](https://github.com/S0l0m0n8und9/toolbAX/releases)**

Download `toolbAX-win-x64.zip` from the assets, extract it anywhere, and run `toolbAX.exe`. It's a **self-contained** Windows x64 build — no .NET runtime install required.

> ⚠️ Releases are currently **unsigned**. Windows SmartScreen will show "Windows protected your PC" — click **More info** → **Run anyway** to proceed. A signed release path is on the roadmap.
>
> To verify your download while the release is unsigned, compare its hash against the published `toolbAX-win-x64.zip.sha256` asset: `Get-FileHash toolbAX-win-x64.zip -Algorithm SHA256`.

### Logs

Each run writes a log to `%LocalAppData%\FoToolbox\logs\toolbax-<date>-<time>.log` — one file per session, capped at the newest 20 and 14 days. It records warnings and errors (failed requests as status + endpoint path, dual-write gateway failures, degraded-mode reasons), and deliberately records no tokens, request/response bodies or headers — a gateway error that quotes its response body on screen is reduced to the status alone in the file. Attach the newest file when reporting a bug; the directory is safe to delete at any time.

The header records which Windows composition backend the run asked for (requested, not negotiated); if the window ever freezes, set `TOOLBAX_COMPOSITION` to `dxgi` (the default), `surface` (maximum compatibility) or `winui` (Avalonia's own default, which deadlocked in [#212](https://github.com/S0l0m0n8und9/toolbAX/issues/212)) before launching to change it without a rebuild — an unrecognised value is ignored rather than fatal.

## Requirements

- Windows 10/11 to run the released build (the app itself is built on cross-platform Avalonia)
- .NET SDK from `global.json` (currently `10.0.201` with `latestPatch` roll-forward)

## Quick Start

The app + its headless tests (cross-platform):

```powershell
dotnet restore .\avalonia\toolBax.slnx
dotnet build .\avalonia\toolBax.slnx -c Release --no-restore
dotnet test .\avalonia\toolBax.slnx -c Release --no-build
```

The shared Core library + its Windows tests (DPAPI vault / MSAL cache):

```powershell
dotnet restore .\FoToolbox.sln
dotnet build .\FoToolbox.sln -c Release --no-restore
dotnet test .\FoToolbox.sln -c Release --no-build
```

Run the app: `dotnet run --project avalonia/toolBax.App`.

## Repository Layout

- `src/FoToolbox.Core/` — shared library: auth, OData client + metadata/query, profiles, secret vault, dual-write gateway
- `avalonia/toolBax.App/` — the Avalonia app (views, view-models, service adapters)
- `avalonia/toolBax.Core/` — UI-side models, service interfaces, dual-write map parser/exporter
- `tests/` — automated tests (`FoToolbox.Tests` for Core; `avalonia/toolBax.App.Tests` for the app)

## Security

If you discover a security issue, please follow `SECURITY.md`.

## License

Licensed under the MIT License. See `LICENSE`.
