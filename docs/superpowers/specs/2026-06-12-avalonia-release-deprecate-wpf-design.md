# Ship the Avalonia app as the release; deprecate the WPF host

**Date:** 2026-06-12
**Status:** Approved (design)
**Scope:** Release pipeline + docs + a small WPF-host startup notice. No functional app changes.

## Problem

The release pipeline (`install/build.ps1` + WiX bundle, driven by `.github/workflows/release.yml`)
publishes the **WPF host** (`src/FoToolbox.Host`). The project has since rebuilt the product as the
cross-platform **Avalonia app** (`avalonia/toolBax.App`), which is where active feature work lands
(e.g. the Query Builder tabbed workspace, PR #139). The Avalonia app is built and tested in CI but has
**no release path** — so the released artifact ships the wrong, now-superseded UI. The first v1.0.0 tag
mistakenly shipped the WPF host and has been rolled back (release + tag deleted).

## Goal

Make the **Avalonia app the released product**, and **deprecate the WPF host** (stop releasing it; keep
it building in CI; add deprecation notices). Then cut **v1.0.0** from the Avalonia app.

## Non-goals

- No WiX/MSI installer for the Avalonia app (a portable self-contained zip instead). Restoring an
  installer/auto-update UX is explicitly out of scope (possible future work).
- No deletion of `src/FoToolbox.Host` or the WPF plugins, and no tracking issue for removal.
- No code signing (releases remain unsigned, matching current state; signing stays on the roadmap).
- No Linux/macOS artifacts yet (win-x64 only).

## Design

### 1. Release pipeline — rewrite `.github/workflows/release.yml`

Replace the entire WiX/WindowsDesktop-runtime/plugin-staging/signing workflow with a self-contained
Avalonia publish:

- **Triggers (unchanged):** `push` on tags `v*`, and `workflow_dispatch` with a `tag` input.
- **Runner:** `windows-latest` (win-x64 self-contained build).
- **Steps:**
  1. `actions/checkout@v4` with `ref: ${{ github.event.inputs.tag || github.ref }}`.
  2. `actions/setup-dotnet@v4` with `global-json-file: global.json`.
  3. Derive the version from the tag: strip a leading `v` (e.g. `v1.0.0` → `1.0.0`); used for
     `-p:Version` / `-p:FileVersion`.
  4. Publish:
     `dotnet publish avalonia/toolBax.App/toolBax.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=<ver> -p:FileVersion=<ver> -o publish`
     (default `EnableWebView2=true` on Windows; single-file self-extract carries the Skia / WebView2 /
     SQLite native libraries.)
  5. Zip the publish output to `FoToolbox-win-x64.zip` (PowerShell `Compress-Archive publish/* …`).
  6. Compose portable-app release notes (see below) to `release-notes.md`.
  7. `softprops/action-gh-release@v2` with:
     - `tag_name: ${{ github.event.inputs.tag || github.ref_name }}`
     - `files: FoToolbox-win-x64.zip`
     - `generate_release_notes: true`
     - `body_path: release-notes.md`
     - `prerelease: ${{ startsWith(<tag>, 'v0.') || contains(<tag>, '-') }}` — **same gating as today**,
       so `v1.0.0` publishes as a full (non-prerelease) release.
- **Permissions:** `contents: write` (unchanged).
- **Release notes blurb:** portable self-contained app; no install needed; unzip and run
  `FoToolbox.exe`; unsigned ⇒ SmartScreen "Windows protected your PC" → **More info** → **Run anyway**;
  signing is on the roadmap. Followed by the auto-generated change log.

### 2. Executable name

Rename the Avalonia app's output to `FoToolbox.exe` via `<AssemblyName>FoToolbox</AssemblyName>` in
`avalonia/toolBax.App/toolBax.App.csproj`, so the public download runs a brand-matching executable
rather than `toolBax.App.exe`. (Namespaces/`x:Class` are unaffected — only the output assembly/exe name
changes.) The release notes and zip reference `FoToolbox.exe`.

> Interpreting the user's "sure" as including this rename. If not wanted, drop §2 and the exe stays
> `toolBax.App.exe` — flag at the spec-review gate.

### 3. WPF host deprecation

- **`README.md`** (root): a deprecation note at the top — the shipping product is the Avalonia app
  (`avalonia/toolBax.App`); the WPF host (`src/FoToolbox.Host`) is deprecated and no longer released.
- **`CLAUDE.md`**: update the build/run guidance and layout notes — the Avalonia app is the released
  product; the WPF host is deprecated (still builds/tests in CI, not shipped). Point "run the app"
  at the Avalonia app.
- **WPF host startup notice** (`src/FoToolbox.Host/App.xaml.cs` + `MainWindow.xaml`/`.xaml.cs`):
  a logged deprecation warning at launch, and a non-blocking banner in the main window
  ("This WPF host is deprecated — the maintained app is the cross-platform Avalonia build."). No
  behavior change, no blocking dialog.
- **CI unchanged:** `build-test`, `ui-tests`, `e2e-tests` keep compiling/testing `src/` (so it doesn't
  rot). Only `release.yml` stops shipping it.

### 4. Versioning & sequencing

- Re-cut as **v1.0.0** (the WPF v1.0.0 release + tag were deleted, freeing the number), stamped into
  the Avalonia assembly from the tag.
- **Order of operations:**
  1. One PR: `release.yml` rewrite + exe rename + deprecation notices.
  2. CI green + Greptile review addressed → merge to `main`.
  3. Tag **`v1.0.0`** on the merge commit and push → the new `release.yml` runs and publishes the
     Avalonia release.
  - The tag must point at a commit that already contains the new `release.yml` (GitHub runs the
    workflow file as it exists at the tagged commit), so the PR merges **before** tagging.

## Testing

- **Local publish smoke (pre-merge):** run the exact `dotnet publish … -r win-x64 --self-contained
  true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` command and confirm it
  produces a runnable `FoToolbox.exe` whose self-extract includes the Skia/WebView2/SQLite natives
  (launch it, or at minimum confirm the single exe is produced and is self-contained).
- **CI:** the existing Avalonia headless tests (`avalonia-tests`) continue to gate the app; the
  `AssemblyName` change must not break them (run `dotnet test avalonia/toolBax.slnx`).
- **Post-release verification:** the `release.yml` run succeeds; the GitHub release for `v1.0.0` is
  **non-prerelease**, not a draft, and has the `FoToolbox-win-x64.zip` asset with generated notes.

## Risks / notes

- **WebView2 runtime** (Evergreen) is a separate system component, generally present on Win10/11; the
  dual-write portal sign-in needs it. Acceptable for a first portable release; documented in notes.
- **Artifact size:** self-contained ≈ 70–100 MB zipped — expected for a runtime-bundled portable app.
- **No auto-update:** the WPF MSI/update-manifest path does not carry over; the Avalonia app has no
  self-update. Out of scope; users download new releases manually for now.
- `install/build.ps1` and the WiX `.wxs` files are left in place (unused by the new release flow) to
  avoid unrelated churn; they can be removed later if/when the WPF host is deleted.
