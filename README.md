# FO Toolbox

FO Toolbox (toolbAX) is a desktop toolbox for Dynamics 365 Finance & Operations (F&O) and Dataverse,
inspired by XrmToolBox-style workflows. It's an [Avalonia](https://avaloniaui.net/) app over a shared
.NET core library.

It provides:
- Environment/profile management, including a header switcher for the active environment
- Entra ID auth in the app (MSAL interactive or client secret); legacy certificate profiles are preserved for explicit replacement
- Data Integrator portal browser sign-in for dual-write; legacy ROPC settings are preserved but not used or offered
- OData metadata exploration and query tools, with cancellable runs
- A POST / write (OData) builder with metadata-backed payload validation
- Dual-write map browser, operations, and compare tooling — Compare reports map presence and reported
  version/state, including Unknown/Ambiguous outcomes
- A Dataverse virtual-tables inspector for the F&O-backed tables
- CSV export

## Status

This repository is in active development. APIs and behavior may change.

The [Dual-write Profiler CLI](profiler/README.md) is **experimental** and is not part of the supported desktop release or current CI/release workflows.

## Download

Releases are published as GitHub Releases:

**[→ Latest release](https://github.com/S0l0m0n8und9/toolbAX/releases/latest)** · **[All releases](https://github.com/S0l0m0n8und9/toolbAX/releases)**

Download `toolbAX-win-x64.zip` from the assets, extract it anywhere, and run `toolbAX.exe`. It's a **self-contained** Windows x64 build — no .NET runtime install required.

> ⚠️ Releases are currently **unsigned**. Follow your organisation's security policy for SmartScreen and download approval. A signed release path is on the roadmap.
>
> To verify your download while the release is unsigned, compare its hash against the published `toolbAX-win-x64.zip.sha256` asset: `Get-FileHash toolbAX-win-x64.zip -Algorithm SHA256`.

### Logs

Each run writes `toolbax-<date>-<time>.log`, usually under `%LocalAppData%\FoToolbox\logs`; the newest 20 and 14 days are retained. Logs omit tokens, request/response bodies, and headers, but endpoint paths or business identifiers may still be present. Review and redact before sharing.

The header records which Windows composition backend the run asked for (requested, not negotiated); if the window ever freezes, set `TOOLBAX_COMPOSITION` to `dxgi` (the default), `surface` (maximum compatibility) or `winui` (Avalonia's own default, which deadlocked in [#212](https://github.com/S0l0m0n8und9/toolbAX/issues/212)) before launching to change it without a rebuild — an unrecognised value is ignored rather than fatal.

## Requirements

### Running the release

- The published Windows x64 zip includes its .NET runtime.
- WebView2 Runtime is a separate prerequisite; see [Microsoft's distribution guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) and follow your organisation's installation/security policy.
- Releases are unsigned; use your organisation's download approval policy.

### Building source

- The SDK in `global.json` (currently `10.0.201` with `latestPatch` roll-forward) is required to build source.

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

See [support and capabilities](docs/support-and-capabilities.md) for prerequisites, product/Core-only boundaries, and evidence limits.

## License

Licensed under the MIT License. See `LICENSE`.
