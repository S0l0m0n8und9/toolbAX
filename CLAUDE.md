# CLAUDE.md

FO Toolbox (toolbAX) — a Windows WPF desktop toolbox for Dynamics 365 Finance & Operations (F&O),
XrmToolBox-style: profile/auth management, OData metadata + query tooling, and a plugin system.

## Build / test

```powershell
dotnet restore .\FoToolbox.sln
dotnet build  .\FoToolbox.sln -c Release --no-restore
dotnet test   .\FoToolbox.sln -c Release --no-build
```

- SDK is pinned in `global.json` (`10.0.201`, `latestPatch`). Targets the .NET 10 Desktop Runtime.
- Run the app from `src/FoToolbox.Host`.
- `TreatWarningsAsErrors` is on **only in CI** (`Directory.Build.props`, gated on `$(CI)`). Don't add
  warnings expecting local builds to catch them — CI will fail even when your machine is green.
- NuGet versions are centrally managed in `Directory.Packages.props` (CPM). Add/bump versions there,
  not in individual `.csproj` files.
- Tests use xUnit. Coverage is collected in CI via `--collect:"XPlat Code Coverage"`; `TestResults/`
  is gitignored — never commit coverage output.

## Layout

- `src/FoToolbox.Core/` — auth (MSAL/Entra), OData client + metadata/query builder, profiles, secret vault, catalog.
- `src/FoToolbox.Host/` — WPF host: app shell, plugin loading (`Plugins/`), themes, views/viewmodels.
- `src/FoToolbox.SDK/` — the public plugin contract (`IFoToolPlugin`, `IPluginContext*`). Plugins reference this.
- `src/FoToolbox.Updater/` — self-update fetch/orchestration.
- `plugins/` — built-in/example plugins (QueryBuilder, ODataPostBuilder, DualWriteMapBrowser, HelloPlugin).
- `tests/FoToolbox.Tests/` — xUnit tests.
- `install/` — WiX installer + update-manifest pipeline.

## Plugin model

- Implement `IFoToolPlugin` (`src/FoToolbox.SDK/Plugins/IFoToolPlugin.cs`): `Id` must match `PluginManifest.json`,
  host calls `InitializeAsync(IPluginContext)` then `CreateTool()` (returns a WPF `UserControl` shown as a tab).
- `IPluginContext` is read-only (OData read client, catalog, logger). Cast to `IPluginContextWrite` /
  `IPluginContextDataverse` / `IPluginContextNavigation` for extended capabilities.
- Plugins load via `AssemblyLoadContext` (`PluginLoadContext`). Trust is governed by `PluginTrustOptions`
  (env vars `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS`, `FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS`).

## Conventions / gotchas

- **Secrets**: stored via `SecretVaultService` — DPAPI (`CurrentUser`) over SQLite. Never log or persist plaintext.
- **Ralph task validation** (`.ralph/tasks.json`): the `validation` value must be a single repo-local wrapper
  token (e.g. `.ralph\validate-build.cmd`). Put real build/test args inside the wrapper. No shell chaining
  (`cd && ...`), no env-var paths (`%USERPROFILE%`), no drive-letter paths (`C:\...`) — the runner treats the
  whole string as a path-like token.
- Releases are currently **unsigned** (SmartScreen warns); signed path is on the roadmap.
