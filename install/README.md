# FO Toolbox Installer (WiX Skeleton)

This folder holds the WiX scaffolding for FO Toolbox MSI packaging and notes on the update pipeline.

Current state
-------------
- MSI is per-user by default (`InstallScope=perUser`) and installs to `%LOCALAPPDATA%\FoToolbox`.
- Components include host binaries and bundled plugins under `%LOCALAPPDATA%\FoToolbox\bin\` (`plugins\` under `bin\`).
- `profile.db` is created on first run under `%LOCALAPPDATA%\FoToolbox\bin\profile.db` and is preserved on uninstall/upgrade (not packaged into the MSI).
- Start menu shortcut (Program Menu\FoToolbox\FO Toolbox) launches `%LOCALAPPDATA%\FoToolbox\bin\FoToolbox.Host.exe`.
- Burn bootstrapper (`Bundle.wxs`) chains .NET Desktop Runtime 8.0 (registry-detected) then `FoToolbox.msi`.
- Runtime bootstrapper uses variables:
  - `NetDesktopRuntimeVersion` (default `8.0.22`)
  - `NetDesktopRuntimeExe` (path to the runtime installer, default `redist/windowsdesktop-runtime-8.0.22-win-x64.exe` when building from `install/`)
  - `NetDesktopRuntimeUrl` (fallback download URL; default aka.ms alias)
  - `FoToolboxMsiPath` (path to the MSI to chain; default `FoToolbox.msi`)
- `FoToolboxFiles.wxs` expects `SourceDir` to point at a published output containing host dependencies and bundled plugins (example below).

Still required from humans
--------------------------
- Provide the actual .NET Desktop Runtime installer file at `NetDesktopRuntimeExe` (or override the path) before building the bundle.
- Confirm/lock ProductCode, UpgradeCode, Bundle UpgradeCode, and Manufacturer/ProductName values for release.
- Supply code-signing certificate/thumbprint; sign MSI/CABs/bundle during build.
- Decide final install scope (per-user vs per-machine) if requirements change.
- Wire MSI build/publish pipeline with the WiX CLI (`wix` v6) using the variables above; verify file paths under `FoToolboxFiles.wxs` match your publish layout.
- Channel strategy: if stable/beta MSIs are needed in parallel, adjust `ProductName`/UpgradeCode pairs accordingly.

## Building with WiX v6 (`wix` CLI)

Important: WiX v6 uses `-d Name=value` (space between `-d` and the `Name=value`), not `-dName=value`.

1. Install the tool once: `dotnet tool install --global wix` (or update with `dotnet tool update --global wix`).
2. Build the MSI:
   ```powershell
   cd install
   wix build .\FoToolbox.wxs .\FoToolboxFiles.wxs `
     -d SourceDir=..\artifacts\FoToolbox `
     -o .\FoToolbox.msi
   ```
3. Build the bootstrapper (bundle) after the MSI exists:
   ```powershell
   wix build .\Bundle.wxs `
     -d FoToolboxMsiPath=FoToolbox.msi `
     -o .\FoToolboxBundle.exe `
     -ext WixToolset.BootstrapperApplications.wixext `
     -ext WixToolset.Util.wixext
   ```

All command-line paths can be changed; key requirement is that `SourceDir` points at the published host output (with bundled plugins) and `FoToolboxMsiPath` points at the MSI you just built.

If you prefer building from the repo root (instead of `cd install`), override the relative paths:
```powershell
wix build install/Bundle.wxs `
  -d FoToolboxMsiPath=install/FoToolbox.msi `
  -d NetDesktopRuntimeExe=install/redist/windowsdesktop-runtime-8.0.22-win-x64.exe `
  -o install/FoToolboxBundle.exe `
  -ext WixToolset.BootstrapperApplications.wixext `
  -ext WixToolset.Util.wixext
```

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
- `profile.db` is created at runtime under `%LOCALAPPDATA%\FoToolbox\bin\profile.db` and is not managed by MSI, so it survives upgrades and uninstall.
- Burn bundle installs .NET Desktop Runtime 8.0 if the registry check under `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` reports a lower version.
- Sign MSI/CABs/bundle with your org's cert; document thumbprint and timestamp URLs.
- Define `SourceDir` at build time to point at your publish output; adjust File Source paths in `FoToolboxFiles.wxs` if layout changes. Replace placeholder GUIDs before release.
- Start menu shortcut is created under Program Menu\FoToolbox and removed on uninstall. Update the description/name if branding changes.
