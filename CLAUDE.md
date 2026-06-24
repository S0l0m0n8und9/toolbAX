# CLAUDE.md

FO Toolbox (toolbAX) — a cross-platform desktop toolbox for Dynamics 365 Finance & Operations (F&O)
and Dataverse, XrmToolBox-style: profile/auth management, OData metadata + query tooling, a POST/write
builder, and dual-write map/operations tooling.

> **The app** is the **Avalonia** app (`avalonia/toolBax.App`), built on a shared **Core** library
> (`src/FoToolbox.Core`). Releases ship a self-contained win-x64 portable zip (`toolbAX-win-x64.zip`)
> built by `.github/workflows/release.yml`. (The original WPF host, SDK, plugins, and WiX installer were
> removed once the Avalonia app reached parity — see git history if you need them.)

## Build / test

The **Avalonia** app + its headless tests (the primary, cross-platform codebase — runs on Linux CI with
no display server):

```powershell
dotnet restore .\avalonia\toolBax.slnx
dotnet build   .\avalonia\toolBax.slnx -c Release --no-restore
dotnet test    .\avalonia\toolBax.slnx -c Release --no-build
```

The shared **Core** library + its xUnit tests (Windows — the DPAPI vault / MSAL cache tests are
Windows-only):

```powershell
dotnet restore .\FoToolbox.sln
dotnet build  .\FoToolbox.sln -c Release --no-restore
dotnet test   .\FoToolbox.sln -c Release --no-build
```

- SDK is pinned in `global.json` (`10.0.201`, `latestPatch`). Targets the .NET 10 Desktop Runtime.
- Run the app: `dotnet run --project avalonia/toolBax.App`.
- `Core` multi-targets `net10.0;net10.0-windows` so the cross-platform Avalonia app and the Windows
  test project both consume it; Windows-only types (DPAPI) are `[SupportedOSPlatform("windows")]`-annotated.
- `TreatWarningsAsErrors` is on **only in CI** (`Directory.Build.props`, gated on `$(CI)`). Don't add
  warnings expecting local builds to catch them — CI will fail even when your machine is green.
- NuGet versions are centrally managed in `Directory.Packages.props` (CPM). Add/bump versions there,
  not in individual `.csproj` files.
- Tests use xUnit. Coverage is collected in CI via `--collect:"XPlat Code Coverage"`; `TestResults/`
  is gitignored — never commit coverage output.

## Layout

- `src/FoToolbox.Core/` — the shared library: auth (MSAL/Entra, `AuthBroker`), OData client + metadata/query
  builder, profiles, `SecretVaultService`, catalog, dual-write gateway. Consumed by the Avalonia app.
- `avalonia/toolBax.App/` — the Avalonia app: views, view-models, and `Core*` service adapters that wire
  `FoToolbox.Core` to the UI (plus `Fake*` services for headless tests / offline design mode).
- `avalonia/toolBax.Core/` — UI-side models, service interfaces, and the dual-write map parser/exporter.
- `tests/FoToolbox.Tests/` — xUnit tests for `FoToolbox.Core` (Windows).
- `avalonia/toolBax.App.Tests/` — headless (Avalonia) tests for the app.

The Avalonia app's tools (Query Builder, POST Builder, Metadata Browser, Dual-Write Map/Operations/Compare)
are **native in-app screens**, not dynamically-loaded plugins.

## Conventions / gotchas

- **Secrets**: stored via `SecretVaultService` — DPAPI (`CurrentUser`) over SQLite. Never log or persist plaintext.
- **Token leakage**: when following a server-supplied absolute URL (e.g. an `@odata.nextLink`) on a
  token-bearing client, gate it with `FoToolbox.Core.Net.RequestOriginGuard` so the env-scoped bearer is
  never sent to a foreign origin (scheme + host + port must match).
- **Ralph task validation** (`.ralph/tasks.json`): the `validation` value must be a single repo-local wrapper
  token (e.g. `.ralph\validate-build.cmd`). Put real build/test args inside the wrapper. No shell chaining
  (`cd && ...`), no env-var paths (`%USERPROFILE%`), no drive-letter paths (`C:\...`) — the runner treats the
  whole string as a path-like token.
- Releases are currently **unsigned** (SmartScreen warns); a published SHA-256 checksum accompanies each
  release zip so downloads can be verified. A signed path is on the roadmap.
