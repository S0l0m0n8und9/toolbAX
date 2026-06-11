# Ship Avalonia, Deprecate WPF — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the cross-platform Avalonia app (`avalonia/toolBax.App`) the released product via a self-contained win-x64 portable zip, deprecate the WPF host (`src/FoToolbox.Host`) without removing it, and cut **v1.0.0**.

**Architecture:** Rewrite `.github/workflows/release.yml` from the WiX/WPF bundle to a `dotnet publish --self-contained` of the Avalonia app, rename that app's output to `FoToolbox.exe`, add WPF deprecation notices (README, CLAUDE.md, a startup log + main-window banner), keep WPF building in CI, then tag `v1.0.0` (which the new workflow publishes).

**Tech Stack:** .NET 10, Avalonia 12, GitHub Actions, `softprops/action-gh-release@v2`, PowerShell (workflow steps), WPF (deprecation banner), xUnit + Avalonia.Headless (existing tests).

**Branch:** `chore/release-avalonia-deprecate-wpf` (already created; spec committed there).

**Commands (from repo root):**
- Avalonia tests: `dotnet test avalonia/toolBax.slnx -c Debug`
- Avalonia publish smoke: see Task 2 Step 4
- WPF host build: `dotnet build src/FoToolbox.Host/FoToolbox.Host.csproj -c Debug`

---

## File Structure

- **Modify** `avalonia/toolBax.App/toolBax.App.csproj` — add `<AssemblyName>FoToolbox</AssemblyName>`. (Task 1)
- **Modify** `avalonia/toolBax.App/App.axaml` — update the `avares://` authority to the new assembly name. (Task 1)
- **Modify** `avalonia/toolBax.App/app.manifest` — update the assembly identity name. (Task 1)
- **Modify** `.github/workflows/release.yml` — full rewrite to self-contained Avalonia publish + zip + release. (Task 2)
- **Modify** `src/FoToolbox.Host/App.xaml.cs` — log a deprecation warning at startup. (Task 3)
- **Modify** `src/FoToolbox.Host/MainWindow.xaml` — wrap the root grid in a `DockPanel` with a top deprecation banner. (Task 3)
- **Modify** `README.md` — rewrite the Download section for the portable zip + add a deprecation note. (Task 4)
- **Modify** `CLAUDE.md` — note Avalonia is the released product; WPF host deprecated. (Task 4)

---

## Task 1: Rename the Avalonia app's output to `FoToolbox.exe`

**Files:**
- Modify: `avalonia/toolBax.App/toolBax.App.csproj`
- Modify: `avalonia/toolBax.App/App.axaml:14`
- Modify: `avalonia/toolBax.App/app.manifest:3`

- [ ] **Step 1: Set the assembly name**

In `avalonia/toolBax.App/toolBax.App.csproj`, add `<AssemblyName>` inside the first `<PropertyGroup>`, right after `<OutputType>WinExe</OutputType>`:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <!-- Public release exe is brand-named (avares:// authority + app.manifest identity follow). -->
    <AssemblyName>FoToolbox</AssemblyName>
```

- [ ] **Step 2: Update the avares:// authority (it is the assembly name)**

In `avalonia/toolBax.App/App.axaml`, line 14, change the resource include so it resolves under the new assembly name:

```xml
        <ResourceInclude Source="avares://FoToolbox/Themes/Tokens.axaml" />
```

- [ ] **Step 3: Update the Win32 manifest identity**

In `avalonia/toolBax.App/app.manifest`, line 3:

```xml
  <assemblyIdentity version="1.0.0.0" name="FoToolbox" />
```

- [ ] **Step 4: Run the Avalonia test suite to prove resources still load**

Run: `dotnet test avalonia/toolBax.slnx -c Debug`
Expected: PASS (all tests). The headless render tests resolve `{StaticResource …}` from `Themes/Tokens.axaml` via the App's merged resources — if the `avares://` authority were wrong, theme loading and those render tests would fail. Green here proves the rename is consistent.

- [ ] **Step 5: Confirm the produced assembly is `FoToolbox`**

Run: `dotnet build avalonia/toolBax.App/toolBax.App.csproj -c Debug`
Then: `Test-Path avalonia/toolBax.App/bin/Debug/net10.0/FoToolbox.dll`
Expected: `True` (the output assembly is now `FoToolbox.dll`, not `toolBax.App.dll`).

- [ ] **Step 6: Commit**

```bash
git add avalonia/toolBax.App/toolBax.App.csproj avalonia/toolBax.App/App.axaml avalonia/toolBax.App/app.manifest
git commit -m "build(avalonia): name the app output FoToolbox (exe + avares authority + manifest)"
```

---

## Task 2: Rewrite `release.yml` to publish the self-contained Avalonia app

**Files:**
- Modify: `.github/workflows/release.yml` (full replacement)

- [ ] **Step 1: Replace the workflow file**

Replace the **entire** contents of `.github/workflows/release.yml` with:

```yaml
name: release

on:
  push:
    tags:
      - 'v*'
  workflow_dispatch:
    inputs:
      tag:
        description: "Tag to publish (e.g. v1.0.0). Must already exist on the repo."
        required: true

permissions:
  contents: write

jobs:
  build:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          ref: ${{ github.event.inputs.tag || github.ref }}

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Derive version from tag
        id: ver
        shell: pwsh
        run: |
          $tag = "${{ github.event.inputs.tag || github.ref_name }}"
          $version = $tag.TrimStart('v')
          "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
          Write-Host "Tag $tag -> version $version"

      - name: Publish self-contained (win-x64)
        shell: pwsh
        run: |
          dotnet publish avalonia/toolBax.App/toolBax.App.csproj `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:Version=${{ steps.ver.outputs.version }} `
            -p:FileVersion=${{ steps.ver.outputs.version }} `
            -o publish

      - name: Verify the executable was produced
        shell: pwsh
        run: |
          if (-not (Test-Path publish/FoToolbox.exe)) {
            throw "publish/FoToolbox.exe was not produced by dotnet publish."
          }
          Get-Item publish/FoToolbox.exe | Select-Object Name, Length, LastWriteTime

      - name: Zip the portable app
        shell: pwsh
        run: Compress-Archive -Path publish/* -DestinationPath FoToolbox-win-x64.zip -Force

      - name: Compose release notes
        shell: pwsh
        run: |
          @'
          ## FOtoolbox — portable app (Avalonia)

          Cross-platform Avalonia build of FOtoolbox. This is a **self-contained** Windows x64 build — no .NET runtime install required.

          **Install:** download `FoToolbox-win-x64.zip`, extract it anywhere, and run `FoToolbox.exe`.

          > ⚠️ **Unsigned.** Windows SmartScreen will show "Windows protected your PC" — click **More info** → **Run anyway**. A signed release path is on the roadmap.

          See the auto-generated change log below for what's new.
          '@ | Out-File -Encoding utf8 release-notes.md

      - name: Publish release with portable zip
        uses: softprops/action-gh-release@v2
        with:
          tag_name: ${{ github.event.inputs.tag || github.ref_name }}
          files: FoToolbox-win-x64.zip
          generate_release_notes: true
          body_path: release-notes.md
          prerelease: ${{ startsWith(github.event.inputs.tag || github.ref_name, 'v0.') || contains(github.event.inputs.tag || github.ref_name, '-') }}
```

- [ ] **Step 2: Sanity-check the YAML structure**

Run: `pwsh -NoProfile -Command "(Get-Content .github/workflows/release.yml -Raw) -match 'softprops/action-gh-release@v2' | Out-Null; if (-not $matches) { throw 'release action missing' }; Write-Host 'release.yml references the release action'"`
Expected: prints the confirmation line (a cheap guard that the file wasn't truncated). Also eyeball that indentation is 2-space and the `prerelease:` expression is intact on one line.

- [ ] **Step 3: Run the local publish smoke (proves the publish command + single-file self-extract work)**

Run:
```powershell
dotnet publish avalonia/toolBax.App/toolBax.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=1.0.0 -p:FileVersion=1.0.0 -o publish-smoke
```
Then: `Test-Path publish-smoke/FoToolbox.exe`
Expected: publish succeeds (exit 0) and `True`. This confirms self-contained single-file packs the Skia/WebView2/SQLite natives without error.

- [ ] **Step 4: Clean up the smoke output (don't commit it)**

Run: `Remove-Item -Recurse -Force publish-smoke`
Expected: no error. (`publish-smoke/` is not tracked; remove so it can't be staged accidentally.)

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci(release): publish the self-contained Avalonia app (portable win-x64 zip) instead of the WiX bundle"
```

---

## Task 3: WPF host deprecation notice (startup log + main-window banner)

**Files:**
- Modify: `src/FoToolbox.Host/App.xaml.cs:14`
- Modify: `src/FoToolbox.Host/MainWindow.xaml` (root layout)

- [ ] **Step 1: Log a deprecation warning at startup**

In `src/FoToolbox.Host/App.xaml.cs`, in `OnStartup`, add the warning right after `AppDiagnostics.Initialize();`:

```csharp
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDiagnostics.Initialize();
        AppDiagnostics.Logger.LogWarning(
            "The WPF host (FoToolbox.Host) is deprecated and no longer released. " +
            "The maintained app is the cross-platform Avalonia build (avalonia/toolBax.App).");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
```

(`Microsoft.Extensions.Logging` is already imported, so `LogWarning` resolves.)

- [ ] **Step 2: Add a deprecation banner above the main window content**

In `src/FoToolbox.Host/MainWindow.xaml`, wrap the root `<Grid>` in a `DockPanel` and dock a banner at the top. Change the opening — replace:

```xml
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
```

with:

```xml
    </Window.Resources>

    <DockPanel>
      <Border DockPanel.Dock="Top" Background="#5A3A00" Padding="10,4">
        <TextBlock Foreground="#FFD08A" FontSize="11.5" TextWrapping="Wrap"
                   Text="This WPF host is deprecated and no longer released. The maintained app is the cross-platform Avalonia build (avalonia/toolBax.App)." />
      </Border>
    <Grid>
        <Grid.RowDefinitions>
```

Then close the `DockPanel` — replace the final lines:

```xml
    </Grid>
</Window>
```

with:

```xml
    </Grid>
    </DockPanel>
</Window>
```

(The root `Grid` is unnamed and the code-behind doesn't reference it, so wrapping is safe. The `<Grid />` filler inside the status bar `DockPanel` at the end is unrelated — only the *last* `</Grid>` before `</Window>` is the root close.)

- [ ] **Step 3: Build the WPF host to confirm the XAML compiles**

Run: `dotnet build src/FoToolbox.Host/FoToolbox.Host.csproj -c Debug`
Expected: Build succeeded, 0 errors. (A malformed wrap would fail the XAML compile.)

- [ ] **Step 4: Commit**

```bash
git add src/FoToolbox.Host/App.xaml.cs src/FoToolbox.Host/MainWindow.xaml
git commit -m "chore(wpf): mark the WPF host deprecated (startup log + main-window banner)"
```

---

## Task 4: Update docs for the Avalonia release + WPF deprecation

**Files:**
- Modify: `README.md` (Download section + deprecation note)
- Modify: `CLAUDE.md` (intro note + run guidance)

- [ ] **Step 1: Rewrite the README Download section**

In `README.md`, replace the Download block (the section from `## Download` through the `install/README.md` line) — replace:

```markdown
## Download

Pre-built installer bundles are published as GitHub Releases:

**[→ Latest release](https://github.com/S0l0m0n8und9/toolbAX/releases/latest)** · **[All releases](https://github.com/S0l0m0n8und9/toolbAX/releases)**

Download `FoToolboxBundle.exe` from the assets, run it, and follow the prompts. The bundle downloads the .NET 10 Desktop Runtime on first install if missing, then installs FOtoolbox to `%LOCALAPPDATA%\FoToolbox\` (per-user).

> ⚠️ Releases are currently **unsigned**. Windows SmartScreen will show "Windows protected your PC" — click **More info** → **Run anyway** to proceed. A signed release path is on the roadmap.

If you want to build the bundle yourself instead of downloading, see [`install/README.md`](install/README.md).
```

with:

```markdown
## Download

The shipping app is the cross-platform **Avalonia** build (`avalonia/toolBax.App`). Releases are published as GitHub Releases:

**[→ Latest release](https://github.com/S0l0m0n8und9/toolbAX/releases/latest)** · **[All releases](https://github.com/S0l0m0n8und9/toolbAX/releases)**

Download `FoToolbox-win-x64.zip` from the assets, extract it anywhere, and run `FoToolbox.exe`. It's a **self-contained** Windows x64 build — no .NET runtime install required.

> ⚠️ Releases are currently **unsigned**. Windows SmartScreen will show "Windows protected your PC" — click **More info** → **Run anyway** to proceed. A signed release path is on the roadmap.

> **Note:** The legacy WPF host (`src/FoToolbox.Host`) is **deprecated** and no longer released. It still builds and is tested in CI, but new work targets the Avalonia app.
```

- [ ] **Step 2: Add a deprecation note to CLAUDE.md intro**

In `CLAUDE.md`, after the opening description paragraph, replace:

```markdown
FO Toolbox (toolbAX) — a Windows WPF desktop toolbox for Dynamics 365 Finance & Operations (F&O),
XrmToolBox-style: profile/auth management, OData metadata + query tooling, and a plugin system.
```

with:

```markdown
FO Toolbox (toolbAX) — a desktop toolbox for Dynamics 365 Finance & Operations (F&O),
XrmToolBox-style: profile/auth management, OData metadata + query tooling, and a plugin system.

> **Shipping app:** the released product is the cross-platform **Avalonia** app (`avalonia/toolBax.App`).
> The original **WPF host** (`src/FoToolbox.Host`) is **deprecated** — it still builds and is tested in CI,
> but it is no longer released and new work targets the Avalonia app. Releases ship a self-contained
> win-x64 portable zip (`FoToolbox-win-x64.zip`) built by `.github/workflows/release.yml`.
```

- [ ] **Step 3: Update the "run the app" guidance in CLAUDE.md**

In `CLAUDE.md`, under the Build / test section, replace:

```markdown
- Run the app from `src/FoToolbox.Host`.
```

with:

```markdown
- Run the released app from `avalonia/toolBax.App` (`dotnet run --project avalonia/toolBax.App`).
  The WPF host in `src/FoToolbox.Host` is deprecated (still builds/tests, not released).
```

- [ ] **Step 4: Verify the docs render and links are intact**

Run: `pwsh -NoProfile -Command "if ((Get-Content README.md -Raw) -notmatch 'FoToolbox-win-x64\.zip') { throw 'README download section not updated' }; if ((Get-Content CLAUDE.md -Raw) -notmatch 'deprecated') { throw 'CLAUDE.md note missing' }; Write-Host 'docs updated'"`
Expected: prints `docs updated`.

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: point downloads at the Avalonia portable zip; mark the WPF host deprecated"
```

---

## Task 5: PR, review, merge

**Files:** none (operational).

- [ ] **Step 1: Push the branch**

Run: `git push -u origin chore/release-avalonia-deprecate-wpf`

- [ ] **Step 2: Open the PR**

Run `gh pr create` with a title like `ci: ship the Avalonia app as the release; deprecate the WPF host` and a body summarizing: release.yml → self-contained Avalonia zip, exe renamed to `FoToolbox.exe`, WPF deprecation notices, WPF still built in CI. Reference the spec `docs/superpowers/specs/2026-06-12-avalonia-release-deprecate-wpf-design.md`.

- [ ] **Step 3: Wait for CI + Greptile, address feedback**

Poll `gh pr checks` until all checks settle (green). Check Greptile's verdict across all three channels (review, comments, PR-body/summary edit). Address any comments per `superpowers:receiving-code-review`; push fixes; reply on the PR.

- [ ] **Step 4: Merge to main**

Once CI is green and Greptile is addressed: `gh pr merge <n> --merge --delete-branch`, then `git checkout main && git pull --ff-only`.

---

## Task 6: Cut the v1.0.0 release

**Files:** none (operational).

- [ ] **Step 1: Confirm the new release.yml is on main**

Run: `pwsh -NoProfile -Command "if ((Get-Content .github/workflows/release.yml -Raw) -notmatch 'FoToolbox-win-x64\.zip') { throw 'main does not yet have the new release.yml' }; Write-Host 'new release.yml present on main'"`
Expected: prints the confirmation. (The tag must point at a commit that has the new workflow.)

- [ ] **Step 2: Tag and push v1.0.0**

```bash
git tag -a v1.0.0 -m "v1.0.0 — first major release (Avalonia app)"
git push origin v1.0.0
```

- [ ] **Step 3: Verify the release workflow + published release**

Poll the `release` workflow run (`gh run list --workflow=release.yml`) until it completes `success`. Then:

Run: `gh release view v1.0.0 --json tagName,isPrerelease,isDraft,assets --jq '{tag:.tagName,prerelease:.isPrerelease,draft:.isDraft,assets:[.assets[].name]}'`
Expected: `tag=v1.0.0`, `prerelease=false`, `draft=false`, assets include `FoToolbox-win-x64.zip`.

- [ ] **Step 4: Finish**

Announce completion with the release URL. Use `superpowers:finishing-a-development-branch` only if there is residual branch state to resolve (the branch was already merged/deleted in Task 5).

---

## Self-Review notes (for the author)

- **Spec coverage:** release.yml rewrite (Task 2 §1) ✓; self-contained win-x64 + single-file + version-from-tag (Task 2) ✓; prerelease gating preserved (Task 2 §1) ✓; unsigned notes (Task 2 §1) ✓; exe rename to FoToolbox incl. avares + manifest ripple (Task 1) ✓; README + CLAUDE.md notices (Task 4) ✓; WPF startup log + banner (Task 3) ✓; CI unchanged / WPF keeps building (no task touches ci.yml — explicit) ✓; v1.0.0 non-prerelease, PR-before-tag ordering (Tasks 5–6) ✓.
- **Placeholder scan:** none — every code/edit step shows exact content; `${{ steps.ver.outputs.version }}` etc. are real workflow expressions, not placeholders.
- **Name consistency:** `FoToolbox` (AssemblyName) ⇒ `FoToolbox.exe`/`FoToolbox.dll` ⇒ `avares://FoToolbox/…` ⇒ `publish/FoToolbox.exe` (workflow verify) ⇒ `FoToolbox-win-x64.zip` (asset) — consistent across Tasks 1, 2, 4.
