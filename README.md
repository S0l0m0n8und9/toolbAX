# FO Toolbox

FO Toolbox is a Windows desktop toolbox for Dynamics 365 Finance & Operations (F&O), inspired by XrmToolBox-style workflows.

It provides:
- Environment/profile management
- Entra ID auth support
- OData metadata exploration and query tools
- Plugin-based extensibility
- CSV export and update plumbing

## Status

This repository is in active development. APIs, plugin contracts, and installer/update behavior may change.

## Download

The shipping app is the cross-platform **Avalonia** build (`avalonia/toolBax.App`). Releases are published as GitHub Releases:

**[→ Latest release](https://github.com/S0l0m0n8und9/toolbAX/releases/latest)** · **[All releases](https://github.com/S0l0m0n8und9/toolbAX/releases)**

Download `toolbAX-win-x64.zip` from the assets, extract it anywhere, and run `toolbAX.exe`. It's a **self-contained** Windows x64 build — no .NET runtime install required.

> ⚠️ Releases are currently **unsigned**. Windows SmartScreen will show "Windows protected your PC" — click **More info** → **Run anyway** to proceed. A signed release path is on the roadmap.

> **Note:** The legacy WPF host (`src/FoToolbox.Host`) is **deprecated** and no longer released. It still builds and is tested in CI, but new work targets the Avalonia app.

## Requirements

- Windows 10/11
- .NET SDK from `global.json` (currently `10.0.201` with `latestPatch` roll-forward)
- Visual Studio 2022 or newer (recommended for WPF development)

## Quick Start

```powershell
dotnet restore .\FoToolbox.sln
dotnet build .\FoToolbox.sln -c Release
dotnet test .\FoToolbox.sln -c Release
```

Run the host from `src/FoToolbox.Host`.

## Ralph Task Validation Commands

When writing `.ralph/tasks.json` entries, keep `validation` commands in a verifier-safe format:

- Use repo-local validator wrappers as a single command token (for example, `.ralph\validate-build.cmd`).
- Put real build/test arguments inside the wrapper script, not in the task `validation` value.
- Do not use shell-chained commands such as `cd ... && dotnet build`.
- Do not use environment-variable paths such as `%USERPROFILE%\...` in `validation`.
- Do not use literal drive-letter paths such as `C:\...` in `validation`.

Why: the Ralph validation runner can treat the full validation string as a path-like command token. Wrapper scripts avoid argument parsing, drive-letter colons, shell chaining, and environment-variable expansion issues.

## Repository Layout

- `src/` - host, core libraries, SDK, updater
- `plugins/` - built-in/example plugins
- `tests/` - automated tests
- `install/` - WiX installer and update pipeline scripts/docs

## Security

If you discover a security issue, please follow `SECURITY.md`.

## Plugin trust

- The 5 bundled plugins are strong-name pinned; the host refuses to load a bundled
  plugin whose assembly has been tampered with.
- Unsigned third-party plugins prompt for consent on first load (Load once / Always
  trust / Don't load). "Always trust" decisions are stored in
  `%LOCALAPPDATA%\FoToolbox\trusted-plugins.json` — delete that file to reset them.
- Set `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS=true` to load all unsigned plugins silently
  (intended for development/CI only).
- Authenticode-signed plugins can be restricted to an allowlist via
  `FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS`.

## License

Licensed under the MIT License. See `LICENSE`.
