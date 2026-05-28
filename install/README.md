# FOtoolbox Installer (WiX Skeleton)

This folder holds the WiX scaffolding for FOtoolbox MSI packaging and notes on the update pipeline.

Current state
-------------
- Installer UI now offers a per-user vs per-machine choice on the **Options** page.
- WiX defaults (if you build directly with `wix build` without overrides) are `ProductName=FOtoolbox`, `Manufacturer=BenJones`, `Version=1.0.0`, `BundleVersion=1.0.0.0`, and `LicenseUrl=https://opensource.org/licenses/MIT`.
- `install/build.ps1` defaults are release-stable for installer identity: it auto-generates a monotonically-increasing version when `-Version` is not provided, but now uses hardcoded per-user/per-machine ProductCode values plus fixed UpgradeCode and Bundle identifiers unless you explicitly override them.
- Components include host binaries and bundled plugins under `%LOCALAPPDATA%\FoToolbox\bin\` (per-user) or `%ProgramFiles%\FoToolbox\bin\` (per-machine).
- `profile.db` is created on first run under `%LOCALAPPDATA%\FoToolbox\profile.db` and is preserved on uninstall/upgrade (not packaged into the MSI).
- Profiles can be managed in-app via the built-in **Profiles** tool; client secrets are stored via DPAPI in `profile.db` (`SecretVault`).
- Start menu shortcut (Program Menu\FOtoolbox\FOtoolbox) launches `%LOCALAPPDATA%\FoToolbox\bin\FoToolbox.Host.exe`.
- Burn bootstrapper (`Bundle.wxs`) chains .NET Desktop Runtime 10.0 (registry-detected) with silent install arguments, then either `FoToolbox.User.msi` or `FoToolbox.Machine.msi` based on the install-scope checkbox.
- Runtime bootstrapper uses variables:
  - `NetDesktopRuntimeVersion` (default `10.0.8`)
  - `NetDesktopRuntimeExe` (path to the runtime installer, default `redist/windowsdesktop-runtime-10.0.8-win-x64.exe` when building from `install/`)
  - `NetDesktopRuntimeUrl` (fallback download URL; default aka.ms alias)
  - `FoToolboxUserMsiPath` (path to the per-user MSI; default `FoToolbox.User.msi`)
  - `FoToolboxMachineMsiPath` (path to the per-machine MSI; default `FoToolbox.Machine.msi`)
- `FoToolboxFiles.wxs` expects `SourceDir` to point at a published output containing host dependencies and bundled plugins (example below).

Still required from humans
--------------------------
- Provide the actual .NET Desktop Runtime installer file at `NetDesktopRuntimeExe` (or override the path) before building the bundle; `install/build.ps1` now fails if it is missing.
- Confirm the locked ProductCode, UpgradeCode, Bundle UpgradeCode, Bundle Id, and Manufacturer/ProductName values still match your release identity before shipping.
- Supply code-signing certificate/thumbprint so `install/build.ps1` can sign MSI and bundle outputs with `signtool` during build; the script now fails fast if signing inputs or `signtool.exe` are missing.
- Decide if you want to lock scope (build only one MSI and skip the scope checkbox).
- Wire MSI build/publish pipeline with the WiX CLI (`wix` v6) using the variables above; verify file paths under `FoToolboxFiles.wxs` match your publish layout.
- Channel strategy: if stable/beta MSIs are needed in parallel, adjust `ProductName`/UpgradeCode pairs accordingly.

## Building with WiX v6 (`wix` CLI)

Important: WiX v6 uses `-d Name=value` (space between `-d` and the `Name=value`), not `-dName=value`.
If you see `error WIX0118: Additional argument '-dSomething=...' was unexpected`, you likely forgot the space.

### Quick build script

If you want a single command that:
- `dotnet publish`es the host into a `SourceDir`,
- copies `HelloPlugin.dll` + `QueryBuilder.dll` into `SourceDir\plugins\`,
- builds `FoToolbox.User.msi` + `FoToolbox.Machine.msi` and (optionally) `FoToolboxBundle.exe`,

run:

```powershell
cd install
.\build.ps1
```

Optional overrides for branding/codes (examples):
```powershell
.\build.ps1 `
  -ProductName "FOtoolbox" `
  -Manufacturer "Your Org" `
  -Version "1.0.0" `
  -ProductCodeUser "{GUID-HERE}" `
  -ProductCodeMachine "{GUID-HERE}" `
  -UpgradeCode "{GUID-HERE}" `
  -BundleUpgradeCode "{GUID-HERE}" `
  -BundleId "YourOrg.FOtoolbox.Bundle" `
  -BundleVersion "1.0.0.0" `
  -LicenseUrl "https://example.com/license"
```

Only override these identifiers when intentionally creating a new product line or update channel. The repository defaults are locked for deterministic upgrades.

Signing inputs:
```powershell
.\build.ps1 `
  -SignCertificateThumbprint "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
```

or

```powershell
.\build.ps1 `
  -SignCertificateFile "C:\certs\footoolbox.pfx" `
  -SignCertificatePassword "<secret>"
```

`-Configuration Release` requires one of these inputs (and a resolvable `signtool.exe`) and fails before packaging if either is absent.

`-Configuration Debug` does not require signing, but **strongly prefers it**: an unsigned Debug bundle cannot upgrade-install over a previously-installed signed bundle — Windows Installer's SecureRepair rejects the MinorUpgrade with `0x80070643`. The workaround is to uninstall the older bundle first. To avoid this rinse-and-repeat, set `$env:FOTOOLBOX_SIGN_THUMBPRINT` (or `$env:FOTOOLBOX_SIGN_CERT_FILE`) once and `build.ps1` will pick it up automatically on every run, keeping signature continuity across builds.

```powershell
# Persist for your shell session (or add to $PROFILE for permanence):
$env:FOTOOLBOX_SIGN_THUMBPRINT = "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
.\build.ps1 -Configuration Debug   # now signs without passing -SignCertificateThumbprint
```

1. Install the tool once: `dotnet tool install --global wix` (or update with `dotnet tool update --global wix`).
2. Install the WiX 6 bundle extensions once if you want to build the bootstrapper:
  ```powershell
  wix extension add -g WixToolset.Bal.wixext/6.0.2
  wix extension add -g WixToolset.Util.wixext/6.0.2
  ```
3. Build the MSIs:
   ```powershell
   cd install
   wix build .\FoToolbox.wxs .\FoToolboxFiles.wxs `
     -d SourceDir=..\artifacts\FoToolbox `
     -d InstallScope=perUser `
     -d InstallRoot=LocalAppDataFolder `
     -d StartMenuRoot=ProgramMenuFolder `
     -d StartMenuRegistryRoot=HKCU `
      -d ProductCode={F057FFFE-9295-4B8D-A60F-41CB15E1ABB6} `
      -o .\FoToolbox.User.msi

  wix build .\FoToolbox.wxs .\FoToolboxFiles.wxs `
    -d SourceDir=..\artifacts\FoToolbox `
    -d InstallScope=perMachine `
    -d InstallRoot=ProgramFiles64Folder `
    -d StartMenuRoot=ProgramMenuFolder `
    -d StartMenuRegistryRoot=HKLM `
    -d ProductCode={6F8A8A0D-7791-4B24-8A2F-D4E8E93FE4AA} `
    -o .\FoToolbox.Machine.msi
   ```
   You can omit `-d ProductCode=...` entirely if you want the defaults from `FoToolbox.wxs`; they are shown here explicitly to document the locked values used for releases.
  4. Build the bootstrapper (bundle) after the MSIs exist:
   ```powershell
   wix build .\Bundle.wxs `
     -d FoToolboxUserMsiPath=FoToolbox.User.msi `
     -d FoToolboxMachineMsiPath=FoToolbox.Machine.msi `
     -o .\FoToolboxBundle.exe `
     -ext WixToolset.Bal.wixext `
     -ext WixToolset.Util.wixext
   ```

All command-line paths can be changed; key requirement is that `SourceDir` points at the published host output (with bundled plugins) and the user/machine MSI paths point at the files you just built.

If you prefer building from the repo root (instead of `cd install`), override the relative paths:
```powershell
wix build install/Bundle.wxs `
  -d FoToolboxUserMsiPath=install/FoToolbox.User.msi `
  -d FoToolboxMachineMsiPath=install/FoToolbox.Machine.msi `
  -d NetDesktopRuntimeExe=install/redist/windowsdesktop-runtime-10.0.8-win-x64.exe `
  -o install/FoToolboxBundle.exe `
  -ext WixToolset.Bal.wixext `
  -ext WixToolset.Util.wixext
```

## Update pipeline (env-configurable)

Runtime env vars used by the host:
- `FOTOOLBOX_UPDATE_MANIFEST` — URL to JSON array of packages.
- `FOTOOLBOX_UPDATE_CHANNEL` — channel name (e.g., `stable`, `beta`); defaults to `stable`.
- `FOTOOLBOX_UPDATE_SIGNER_THUMBPRINT` — optional Authenticode signer thumbprint to enforce for staged MSIs.

Manifest JSON shape (example):
```json
[
  { "channel": "stable", "version": "1.0.0", "uri": "https://cdn.example.com/footoolbox-1.0.0.msi", "hash": "ABC123..." },
  {
    "channel": "beta",
    "version": "1.1.0",
    "uri": "https://cdn.example.com/footoolbox-1.1.0.msi",
    "hash": "DEF456...",
    "rollbackUri": "https://cdn.example.com/footoolbox-1.0.0.msi",
    "rollbackHash": "ABC123..."
  }
]
```
The updater will pick the latest entry per channel (highest `version` when parseable, otherwise last entry), SHA256-verify the payload, and stage it under `updates/staged.msi`. If `rollbackUri` + `rollbackHash` are provided, it will also stage `updates/rollback.msi`.
For per-machine installs, ensure the manifest points at the per-machine MSI. For per-user installs, point at the per-user MSI.
See `install/update-manifest.sample.json` for a full example file.
Note: `version` uses `System.Version` parsing, so stick to numeric formats like `0.2.0` or rely on array ordering for pre-release labels.

Local update dev
----------------
1. Build MSIs and copy them to `install/` (the default output from `.\build.ps1` already lands here).
2. Compute the SHA256 for the MSI you want to test (user or machine):
   ```powershell
   Get-FileHash -Algorithm SHA256 .\FoToolbox.User.msi
   ```
3. Update `install/update-manifest.local.json` with the hash and correct MSI filename.
4. Run the local update server:
   ```powershell
   cd install
   .\serve-update.ps1 -Root .\
   ```
5. Launch FOtoolbox with:
   - `FOTOOLBOX_UPDATE_MANIFEST=http://localhost:8787/update-manifest.local.json`
   - `FOTOOLBOX_UPDATE_CHANNEL=stable`

Versioning note
---------------
- MSI `Version` must be three-part (e.g., `1.0.0`).
- Bundle `BundleVersion` can be four-part (e.g., `1.0.0.0`).
- `install/build.ps1` will trim a four-part `-Version` to three-part for MSI and reuse the original as `BundleVersion` if none is provided.
- To upgrade in-place (no uninstall), keep `UpgradeCode` stable and increase MSI `Version`. This repo now locks installer GUIDs in source for deterministic release packaging.

## Packaging notes

- ProductCode values, UpgradeCode, Bundle Id, and Bundle UpgradeCode are locked in source for deterministic packaging; only override them intentionally for a new product line.
- `profile.db` is created at runtime under `%LOCALAPPDATA%\FoToolbox\bin\profile.db` and is not managed by MSI, so it survives upgrades and uninstall.
- Burn bundle installs .NET Desktop Runtime 8.0 if the registry check under `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` reports a lower version.
- Sign MSI/CABs/bundle with your org's cert; document thumbprint and timestamp URLs.
- Define `SourceDir` at build time to point at your publish output; adjust File Source paths in `FoToolboxFiles.wxs` if layout changes.
- Start menu shortcut is created under Program Menu\FoToolbox and removed on uninstall. Update the description/name if branding changes.
