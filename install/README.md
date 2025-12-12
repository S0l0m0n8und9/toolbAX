# FO Toolbox Installer (WiX Skeleton)

This folder holds the WiX scaffolding for FO Toolbox MSI packaging and notes on the update pipeline.

Current state
-------------
- MSI is per-user by default (`InstallScope=perUser`) and installs to `%LOCALAPPDATA%\FoToolbox`.
- Components include host binaries, plugins, and `data\profile.db` (Permanent + NeverOverwrite to preserve user data).
- Start menu shortcut (Program Menu\FoToolbox\FO Toolbox) launches `FoToolbox.Host.exe`.
- Burn bootstrapper (`Bundle.wxs`) chains .NET Desktop Runtime 8.0 (registry-detected) then `FoToolbox.msi`.
- Runtime bootstrapper uses variables:
  - `NetDesktopRuntimeVersion` (default `8.0.0`)
  - `NetDesktopRuntimeExe` (path to the runtime installer, default `install/redist/windowsdesktop-runtime-8.0.0-win-x64.exe`)
  - `NetDesktopRuntimeUrl` (fallback download URL; default aka.ms alias)
  - `FoToolboxMsiPath` (path to the MSI to chain; default `FoToolbox.msi`)
- `FoToolboxFiles.wxs` expects `SourceDir` to point at a published output (e.g., `-dSourceDir=..\src\FoToolbox.Host\bin\Release\net8.0-windows\publish`).

Still required from humans
--------------------------
- Provide the actual .NET Desktop Runtime installer file at `NetDesktopRuntimeExe` (or override the path) before building the bundle.
- Confirm/lock ProductCode, UpgradeCode, Bundle UpgradeCode, and Manufacturer/ProductName values for release.
- Supply code-signing certificate/thumbprint; sign MSI/CABs/bundle during build.
- Decide final install scope (per-user vs per-machine) if requirements change.
- Wire MSI build/publish pipeline (candle/light or msbuild) with the variables above; verify file paths under `FoToolboxFiles.wxs` match your publish layout.
- Channel strategy: if stable/beta MSIs are needed in parallel, adjust `ProductName`/UpgradeCode pairs accordingly.

## Update pipeline (env-configurable)

Runtime env vars used by the host:
- `FOTOOLBOX_UPDATE_MANIFEST` — URL to JSON array of packages.
- `FOTOOLBOX_UPDATE_CHANNEL` — channel name (e.g., `stable`, `beta`); defaults to `stable`.

Manifest JSON shape (example):
```json
[
  { "channel": "stable", "uri": "https://cdn.example.com/fo-toolbox-0.2.0.msi", "hash": "ABC123..." },
  { "channel": "beta",   "uri": "https://cdn.example.com/fo-toolbox-0.3.0-beta.msi", "hash": "DEF456..." }
]
```
The updater will pick the latest entry per channel, SHA256-verify the payload, and stage it under `updates/`.

## Packaging notes

- Generate and lock ProductCode/UpgradeCode GUIDs before shipping. Bundle UpgradeCode is currently a placeholder; change once and keep it stable.
- `profile.db` is installed under `%LOCALAPPDATA%\FoToolbox\data\` and marked Permanent/NeverOverwrite so user data survives upgrades and uninstall.
- Burn bundle installs .NET Desktop Runtime 8.0 if the registry check under `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` reports a lower version.
- Sign MSI/CABs/bundle with your org’s cert; document thumbprint and timestamp URLs.
- Define `SourceDir` at build time to point at your publish output; adjust File Source paths in `FoToolboxFiles.wxs` if layout changes. Replace placeholder GUIDs before release.
- Start menu shortcut is created under Program Menu\FoToolbox and removed on uninstall. Update the description/name if branding changes.
