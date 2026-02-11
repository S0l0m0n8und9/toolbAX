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

## Repository Layout

- `src/` - host, core libraries, SDK, updater
- `plugins/` - built-in/example plugins
- `tests/` - automated tests
- `install/` - WiX installer and update pipeline scripts/docs

## Security

If you discover a security issue, please follow `SECURITY.md`.

## License

Licensed under the MIT License. See `LICENSE`.
