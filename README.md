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

## Requirements

- Windows 10/11
- .NET SDK from `global.json` (currently `9.0.306` with `latestMinor` roll-forward)
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

## License

Licensed under the MIT License. See `LICENSE`.
