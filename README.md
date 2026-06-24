# FO Toolbox

FO Toolbox (toolbAX) is a desktop toolbox for Dynamics 365 Finance & Operations (F&O) and Dataverse,
inspired by XrmToolBox-style workflows. It's an [Avalonia](https://avaloniaui.net/) app over a shared
.NET core library.

It provides:
- Environment/profile management
- Entra ID auth (MSAL interactive, client secret, certificate)
- OData metadata exploration and query tools
- A POST / write (OData) builder
- Dual-write map browser, operations, and compare tooling
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

## Ralph Task Validation Commands

When writing `.ralph/tasks.json` entries, keep `validation` commands in a verifier-safe format:

- Use repo-local validator wrappers as a single command token (for example, `.ralph\validate-build.cmd`).
- Put real build/test arguments inside the wrapper script, not in the task `validation` value.
- Do not use shell-chained commands such as `cd ... && dotnet build`.
- Do not use environment-variable paths such as `%USERPROFILE%\...` in `validation`.
- Do not use literal drive-letter paths such as `C:\...` in `validation`.

Why: the Ralph validation runner can treat the full validation string as a path-like command token. Wrapper scripts avoid argument parsing, drive-letter colons, shell chaining, and environment-variable expansion issues.

## Security

If you discover a security issue, please follow `SECURITY.md`.

## License

Licensed under the MIT License. See `LICENSE`.
