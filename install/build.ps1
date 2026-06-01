param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$SourceDir = "",
    [string]$MsiPath = "",
    [string]$UserMsiPath = "",
    [string]$MachineMsiPath = "",
    [string]$BundlePath = "",
    [string]$RuntimeExe = "",
    [string]$RuntimeVersion = "",
    [string]$ProductName = "",
    [string]$Manufacturer = "",
    [string]$Version = "",
    [string]$ProductCode = "",
    [string]$ProductCodeUser = "",
    [string]$ProductCodeMachine = "",
    [string]$UpgradeCode = "",
    [string]$InstallScope = "",
    [string]$BundleName = "",
    [string]$BundleVersion = "",
    [string]$BundleManufacturer = "",
    [string]$BundleUpgradeCode = "",
    [string]$BundleId = "",
    [string]$LicenseUrl = "",
    [string]$SignToolPath = "",
    [Alias("CertThumbprint")]
    [string]$SignCertificateThumbprint = "",
    [string]$SignCertificateFile = "",
    [string]$SignCertificatePassword = "",
    [string]$SignTimestampUrl = "http://timestamp.digicert.com",
    [string]$SignFileDigest = "sha256",
    [string]$SignTimestampDigest = "sha256"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Fall back to environment variables so developers don't have to pass -SignCertificateThumbprint
# on every Debug invocation. Setting $env:FOTOOLBOX_SIGN_THUMBPRINT once in your profile is
# enough to keep dev builds signature-continuous with installed signed predecessors, which is
# what SecureRepair requires to allow MinorUpgrade installs over an existing signed package.
if ([string]::IsNullOrWhiteSpace($SignCertificateThumbprint) -and -not [string]::IsNullOrWhiteSpace($env:FOTOOLBOX_SIGN_THUMBPRINT)) {
    $SignCertificateThumbprint = $env:FOTOOLBOX_SIGN_THUMBPRINT
}
if ([string]::IsNullOrWhiteSpace($SignCertificateFile) -and -not [string]::IsNullOrWhiteSpace($env:FOTOOLBOX_SIGN_CERT_FILE)) {
    $SignCertificateFile = $env:FOTOOLBOX_SIGN_CERT_FILE
}

$devVersionHelp = @"
MSI version constraints (Windows Installer):
- ProductVersion is 3-part: major.minor.build
- major/minor: 0-255
- build: 0-65535
"@

function New-DevVersion {
    <#
      Generates a monotonically-increasing version that fits MSI constraints without needing a persisted counter.

      Scheme (UTC-based to avoid DST issues):
      - major: year % 100 (0-99)
      - minor: floor((dayOfYear-1) / 2) (0-182)
      - build: ((dayOfYear-1) % 2) * 28800 + floor(secondsSinceMidnight / 3) (0-57599)
      - revision (bundle/file only): secondsSinceMidnight % 3 (0-2)
    #>
    $now = [DateTime]::UtcNow
    $yy = $now.Year % 100
    $dayIndex = [int]$now.DayOfYear - 1
    $minor = [int][math]::Floor($dayIndex / 2.0)
    $seconds = [int][math]::Floor($now.TimeOfDay.TotalSeconds)
    $build = (($dayIndex % 2) * 28800) + [int][math]::Floor($seconds / 3.0)
    $revision = $seconds % 3

    if ($yy -gt 255 -or $minor -gt 255 -or $build -gt 65535) {
        throw "Auto-version out of MSI range (computed $yy.$minor.$build). $devVersionHelp"
    }

    return @{
        Msi = "$yy.$minor.$build"
        Bundle = "$yy.$minor.$build.$revision"
    }
}

$installDir = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $installDir "..") | Select-Object -ExpandProperty Path
$defaultProductCodeUser = "{CE886CFF-6D3A-4ABE-BEA8-56444B3C6FB5}"
$defaultProductCodeMachine = "{F9FE1558-8DCD-48F2-B208-12052B157604}"
$defaultUpgradeCode = "{5E38A1ED-8CDD-4069-81F2-04C4DF076C11}"
$defaultBundleUpgradeCode = "{ED449692-157D-46FC-A96D-AFB178DF60F1}"
$defaultBundleId = "BenJones.FOtoolbox.Bundle"

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot "artifacts\\FoToolbox"
}
if ([string]::IsNullOrWhiteSpace($UserMsiPath)) {
    $UserMsiPath = if (-not [string]::IsNullOrWhiteSpace($MsiPath)) { $MsiPath } else { Join-Path $installDir "FoToolbox.User.msi" }
}
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = $UserMsiPath
}
if ([string]::IsNullOrWhiteSpace($MachineMsiPath)) {
    $MachineMsiPath = Join-Path $installDir "FoToolbox.Machine.msi"
}
if ([string]::IsNullOrWhiteSpace($BundlePath)) {
    $BundlePath = Join-Path $installDir "FoToolboxBundle.exe"
}
if ([string]::IsNullOrWhiteSpace($RuntimeExe)) {
    $RuntimeExe = Join-Path $installDir "redist\\windowsdesktop-runtime-10.0.8-win-x64.exe"
}

if ([string]::IsNullOrWhiteSpace($ProductCodeUser)) {
    $ProductCodeUser = if (-not [string]::IsNullOrWhiteSpace($ProductCode)) { $ProductCode } else { $defaultProductCodeUser }
}
if ([string]::IsNullOrWhiteSpace($ProductCodeMachine)) {
    $ProductCodeMachine = $defaultProductCodeMachine
}
if ([string]::IsNullOrWhiteSpace($UpgradeCode)) {
    $UpgradeCode = $defaultUpgradeCode
}
if ([string]::IsNullOrWhiteSpace($BundleUpgradeCode)) {
    $BundleUpgradeCode = $defaultBundleUpgradeCode
}
if ([string]::IsNullOrWhiteSpace($BundleId)) {
    $BundleId = $defaultBundleId
}

# If no version was supplied, generate one that satisfies MSI rules and changes every few seconds.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $auto = New-DevVersion
    $Version = $auto.Bundle
    if ([string]::IsNullOrWhiteSpace($BundleVersion)) {
        $BundleVersion = $auto.Bundle
    }
}

$msiVersion = $Version
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $parts = $Version.Split(".")
    if ($parts.Length -ge 4) {
        $msiVersion = ($parts[0..2] -join ".")
        if ([string]::IsNullOrWhiteSpace($BundleVersion)) {
            $BundleVersion = $Version
        }
    } elseif ($parts.Length -eq 3 -and [string]::IsNullOrWhiteSpace($BundleVersion)) {
        $BundleVersion = "$Version.0"
    }
}

$fileVersion = if (-not [string]::IsNullOrWhiteSpace($BundleVersion)) { $BundleVersion } else { "$msiVersion.0" }
$assemblyVersion = "1.0.0.0"

Write-Host "Repo root: $repoRoot"
Write-Host "Configuration: $Configuration"
Write-Host "SourceDir: $SourceDir"
Write-Host "User MSI: $UserMsiPath"
Write-Host "Machine MSI: $MachineMsiPath"
Write-Host "Bundle: $BundlePath"
Write-Host "RuntimeExe: $RuntimeExe"
if (-not [string]::IsNullOrWhiteSpace($RuntimeVersion)) { Write-Host "RuntimeVersion: $RuntimeVersion" }
if (-not [string]::IsNullOrWhiteSpace($ProductName)) { Write-Host "ProductName: $ProductName" }
if (-not [string]::IsNullOrWhiteSpace($Manufacturer)) { Write-Host "Manufacturer: $Manufacturer" }
if (-not [string]::IsNullOrWhiteSpace($Version)) { Write-Host "Version: $Version (MSI: $msiVersion, File: $fileVersion)" }
Write-Host "AssemblyVersion: $assemblyVersion"
if (-not [string]::IsNullOrWhiteSpace($ProductCode)) { Write-Host "ProductCode: $ProductCode" }
if (-not [string]::IsNullOrWhiteSpace($ProductCodeUser)) { Write-Host "ProductCodeUser: $ProductCodeUser" }
if (-not [string]::IsNullOrWhiteSpace($ProductCodeMachine)) { Write-Host "ProductCodeMachine: $ProductCodeMachine" }
if (-not [string]::IsNullOrWhiteSpace($UpgradeCode)) { Write-Host "UpgradeCode: $UpgradeCode" }
if (-not [string]::IsNullOrWhiteSpace($InstallScope)) { Write-Host "InstallScope: $InstallScope" }
if (-not [string]::IsNullOrWhiteSpace($BundleName)) { Write-Host "BundleName: $BundleName" }
if (-not [string]::IsNullOrWhiteSpace($BundleVersion)) { Write-Host "BundleVersion: $BundleVersion" }
if (-not [string]::IsNullOrWhiteSpace($BundleManufacturer)) { Write-Host "BundleManufacturer: $BundleManufacturer" }
if (-not [string]::IsNullOrWhiteSpace($BundleUpgradeCode)) { Write-Host "BundleUpgradeCode: $BundleUpgradeCode" }
if (-not [string]::IsNullOrWhiteSpace($BundleId)) { Write-Host "BundleId: $BundleId" }
if (-not [string]::IsNullOrWhiteSpace($LicenseUrl)) { Write-Host "LicenseUrl: $LicenseUrl" }

function Get-InstalledFoToolboxProducts {
    $uninstallRoots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    $results = @()
    foreach ($root in $uninstallRoots) {
        $scope = if ($root -like "HKCU:*") { "User" } else { "Machine" }
        $entries = Get-ItemProperty -Path $root -ErrorAction SilentlyContinue |
            Where-Object {
                $hasDisplayName = $_.PSObject.Properties.Name -contains "DisplayName"
                $hasProductCode = $_.PSObject.Properties.Name -contains "PSChildName"
                if (-not $hasDisplayName -or -not $hasProductCode) {
                    return $false
                }

                ($_.DisplayName -eq "FOtoolbox" -or $_.DisplayName -eq "FoToolbox") -and
                -not [string]::IsNullOrWhiteSpace($_.PSChildName)
            }

        foreach ($entry in $entries) {
            $results += [PSCustomObject]@{
                Scope = $scope
                ProductCode = $entry.PSChildName
                Version = $entry.DisplayVersion
            }
        }
    }

    return $results
}

function Write-InstallPreflight {
    $installed = Get-InstalledFoToolboxProducts
    if (-not $installed -or $installed.Count -eq 0) {
        return
    }

    Write-Host "`nDetected existing FOtoolbox MSI registration(s):" -ForegroundColor Yellow
    foreach ($item in $installed) {
        Write-Host " - Scope=$($item.Scope), Version=$($item.Version), ProductCode=$($item.ProductCode)" -ForegroundColor Yellow
    }

    Write-Host "Potential reinstall blocker: existing registrations with static ProductCode from older builds can trigger MSI error 1638." -ForegroundColor Yellow
    Write-Host "If install fails, uninstall old registrations first:" -ForegroundColor Yellow
    foreach ($item in $installed) {
        Write-Host " msiexec /x $($item.ProductCode)" -ForegroundColor Yellow
    }
    Write-Host "For machine-scope entries, run the uninstall command from an elevated terminal." -ForegroundColor Yellow
}

function Get-SignToolPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        return $null
    }

    return Get-ChildItem $kitsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Test-SigningRequested {
    return -not [string]::IsNullOrWhiteSpace($SignCertificateThumbprint) -or -not [string]::IsNullOrWhiteSpace($SignCertificateFile)
}

function Assert-SigningConfiguration {
    if (Test-SigningRequested) {
        # Always validate the inputs when signing is requested, regardless of configuration —
        # a dev build that claims to sign should actually be able to.
        if (-not [string]::IsNullOrWhiteSpace($SignCertificateFile) -and -not (Test-Path $SignCertificateFile)) {
            throw "Signing certificate file was not found at $SignCertificateFile."
        }

        $signTool = Get-SignToolPath -ExplicitPath $SignToolPath
        if ([string]::IsNullOrWhiteSpace($signTool)) {
            throw "signtool.exe was not found. Install the Windows SDK or pass -SignToolPath."
        }
        return
    }

    # No signing configured.
    if ($Configuration -eq "Release") {
        throw "Signing is required for release installer outputs. Pass -SignCertificateThumbprint or -SignCertificateFile, or set `$env:FOTOOLBOX_SIGN_THUMBPRINT before running install/build.ps1 -Configuration Release."
    }

    # Debug builds without a cert are allowed, but warn loudly — an unsigned upgrade over a
    # signed installed predecessor will be rejected by Windows Installer's SecureRepair check
    # with 0x80070643. Set `$env:FOTOOLBOX_SIGN_THUMBPRINT to your dev cert to make this go away
    # for every subsequent build.
    Write-Warning "Building unsigned installer artifacts (Configuration=Debug, no signing cert configured)."
    Write-Warning "  These artifacts CANNOT upgrade-install over a previously-installed signed bundle:"
    Write-Warning "  SecureRepair will refuse the MinorUpgrade with 0x80070643."
    Write-Warning "  Set `$env:FOTOOLBOX_SIGN_THUMBPRINT to your dev cert thumbprint to sign dev builds too,"
    Write-Warning "  or uninstall any existing FOtoolbox bundle before running the unsigned bundle."
}

$script:SignedArtifacts = New-Object System.Collections.Generic.List[object]

function Sign-Output {
    param(
        [string]$FilePath,
        [string]$Description
    )

    if (-not (Test-SigningRequested)) {
        Write-Host "Skipping signing for $Description; no signing certificate was configured (Debug build)."
        return
    }

    $signTool = Get-SignToolPath -ExplicitPath $SignToolPath

    $signArgs = @(
        "sign",
        "/fd", $SignFileDigest,
        "/td", $SignTimestampDigest,
        "/tr", $SignTimestampUrl,
        "/d", $Description
    )

    $thumbprintForLog = $SignCertificateThumbprint
    if (-not [string]::IsNullOrWhiteSpace($SignCertificateFile)) {
        $signArgs += @("/f", $SignCertificateFile)
        if (-not [string]::IsNullOrWhiteSpace($SignCertificatePassword)) {
            $signArgs += @("/p", $SignCertificatePassword)
        }
        if ([string]::IsNullOrWhiteSpace($thumbprintForLog)) {
            $thumbprintForLog = "<from PFX: $SignCertificateFile>"
        }
    } else {
        $signArgs += @("/sha1", $SignCertificateThumbprint)
    }

    $signArgs += $FilePath

    Write-Host "Signing $Description..."
    & $signTool @signArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $FilePath with exit code $LASTEXITCODE."
    }

    $script:SignedArtifacts.Add([PSCustomObject]@{
        File        = $FilePath
        Description = $Description
        Thumbprint  = $thumbprintForLog
    })
}

function Sign-BurnBundle {
    param(
        [string]$BundlePath,
        [string]$Description
    )

    if (-not (Test-SigningRequested)) {
        Write-Host "Skipping signing for $Description; no signing certificate was configured (Debug build)."
        return
    }

    # Burn bundles require a detach -> sign engine -> reattach -> sign bundle
    # sequence so the bundle's attached container offset survives signtool's PE rewrite.
    # Signing the bundle directly corrupts the container and breaks payload extraction
    # at install time (Burn falls back to "Browse for source" with no guidance).
    $detachedEngine = [System.IO.Path]::ChangeExtension($BundlePath, ".engine.exe")
    Write-Host "Detaching bundle engine for $Description..."
    wix burn detach $BundlePath -engine $detachedEngine | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "wix burn detach failed for $BundlePath (exit $LASTEXITCODE)."
    }

    Sign-Output -FilePath $detachedEngine -Description "$Description (detached engine)"

    Write-Host "Reattaching signed engine to bundle..."
    wix burn reattach $BundlePath -engine $detachedEngine -o $BundlePath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "wix burn reattach failed for $BundlePath (exit $LASTEXITCODE)."
    }

    Sign-Output -FilePath $BundlePath -Description $Description

    Remove-Item $detachedEngine -Force -ErrorAction SilentlyContinue
}

function Write-SignedArtifactsSummary {
    if ($script:SignedArtifacts.Count -eq 0) {
        Write-Host "`nNo artifacts were signed during this build."
        return
    }

    Write-Host "`nSigned artifact summary:"
    foreach ($entry in $script:SignedArtifacts) {
        Write-Host (" - {0} (Thumbprint: {1}) [{2}]" -f $entry.File, $entry.Thumbprint, $entry.Description)
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required but was not found on PATH."
}
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix is required but was not found on PATH. Install with: dotnet tool install --global wix"
}

Write-InstallPreflight

Assert-SigningConfiguration

New-Item -ItemType Directory -Force -Path $SourceDir | Out-Null

Write-Host "`nPublishing host..."
dotnet publish (Join-Path $repoRoot "src\\FoToolbox.Host\\FoToolbox.Host.csproj") `
    -c $Configuration `
    -o $SourceDir `
    -p:Version=$msiVersion `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$fileVersion | Out-Host

$pluginsOut = Join-Path $SourceDir "plugins"
# Clear any previously-staged plugin output so the installer is reproducible and never ships stale
# files from an earlier build (e.g. a plugin that was removed, or files staged under a wrong path).
if (Test-Path $pluginsOut) {
    Remove-Item $pluginsOut -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $pluginsOut | Out-Null
$bundledPlugins = @(
    "HelloPlugin",
    "QueryBuilder",
    "TableEntityBrowser",
    "ODataPostBuilder",
    "DualWriteMapBrowser",
    "DualWriteOperations",
    "DualWriteCompare"
)

Write-Host "`nBuilding plugins..."
dotnet clean (Join-Path $repoRoot "plugins\\HelloPlugin\\HelloPlugin.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\HelloPlugin\\HelloPlugin.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\QueryBuilder\\QueryBuilder.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\QueryBuilder\\QueryBuilder.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\TableEntityBrowser\\TableEntityBrowser.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\TableEntityBrowser\\TableEntityBrowser.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\ODataPostBuilder\\ODataPostBuilder.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\ODataPostBuilder\\ODataPostBuilder.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\DualWriteMapBrowser\\DualWriteMapBrowser.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\DualWriteMapBrowser\\DualWriteMapBrowser.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\DualWriteOperations\\DualWriteOperations.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\DualWriteOperations\\DualWriteOperations.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet clean (Join-Path $repoRoot "plugins\\DualWriteCompare\\DualWriteCompare.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\DualWriteCompare\\DualWriteCompare.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host

Write-Host "`nCopying plugin binaries to SourceDir..."

# Files already provided by the host shell (SDK/Core/shared managed assemblies). A plugin must NOT
# carry its own copy of these: a duplicate FoToolbox.SDK in a plugin's load context breaks the
# IFoToolPlugin type identity and the plugin silently fails to load. Treat the published host output
# as the source of truth for "host-provided" and only stage a plugin dependency the host does NOT
# already ship (e.g. WebView2 for DualWriteOperations' interactive sign-in).
$hostProvided = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($hostDll in Get-ChildItem -Path $SourceDir -File -Filter *.dll) {
    [void]$hostProvided.Add($hostDll.Name)
}

foreach ($pluginName in $bundledPlugins) {
    $legacyFlatPath = Join-Path $pluginsOut "$pluginName.dll"
    if (Test-Path $legacyFlatPath) {
        Remove-Item $legacyFlatPath -Force
    }

    $pluginDirectory = Join-Path $pluginsOut $pluginName
    New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null

    $pluginBin = Join-Path $repoRoot "plugins\\$pluginName\\bin\\$Configuration\\net10.0-windows"
    $primaryDll = "$pluginName.dll"

    # Always stage the primary plugin assembly.
    Copy-Item (Join-Path $pluginBin $primaryDll) -Destination (Join-Path $pluginDirectory $primaryDll) -Force

    # Stage private runtime dependencies the host does not provide (managed DLLs + native loaders
    # such as WebView2Loader.dll) so the plugin's load context can resolve them. Skip host-provided
    # managed assemblies and build-time artifacts.
    foreach ($dep in Get-ChildItem -Path $pluginBin -File -Filter *.dll) {
        if ($dep.Name -ieq $primaryDll) { continue }
        if ($hostProvided.Contains($dep.Name)) { continue }
        Copy-Item $dep.FullName -Destination (Join-Path $pluginDirectory $dep.Name) -Force
        Write-Host "   staged private dependency $($dep.Name) for $pluginName"
    }

    # Stage the win-x64 native loaders the plugin needs but the host does not already provide
    # (e.g. WebView2Loader.dll for interactive sign-in), placed next to the managed assembly where
    # the WebView2 SDK probes. The app ships win-x64 only, so cross-platform natives are skipped;
    # e_sqlite3 is host-provided at the app root and must not be duplicated per plugin.
    $nativeDir = Join-Path $pluginBin "runtimes\\win-x64\\native"
    if (Test-Path $nativeDir) {
        foreach ($nativeFile in Get-ChildItem -Path $nativeDir -File) {
            if ($nativeFile.Name -ieq "e_sqlite3.dll") { continue }
            if ($hostProvided.Contains($nativeFile.Name)) { continue }
            Copy-Item $nativeFile.FullName -Destination (Join-Path $pluginDirectory $nativeFile.Name) -Force
            Write-Host "   staged native loader $($nativeFile.Name) for $pluginName"
        }
    }
}

Write-Host "Verifying canonical plugin staging layout..."
foreach ($pluginName in $bundledPlugins) {
    $expectedPath = Join-Path (Join-Path $pluginsOut $pluginName) "$pluginName.dll"
    if (-not (Test-Path $expectedPath)) {
        throw "Bundled plugin staging assertion failed: expected $expectedPath"
    }
}

$sqliteNativeSource = Join-Path $SourceDir "runtimes\\win-x64\\native\\e_sqlite3.dll"
$sqliteNativeDestination = Join-Path $SourceDir "e_sqlite3.dll"
if ((-not (Test-Path $sqliteNativeDestination)) -and (Test-Path $sqliteNativeSource)) {
    Write-Host "Staging native SQLite dependency..."
    Copy-Item $sqliteNativeSource -Destination $sqliteNativeDestination -Force
}

function Build-Msi {
    param(
        [string]$OutputPath,
        [string]$Scope,
        [string]$ProductCodeValue,
        [string]$InstallRoot,
        [string]$StartMenuRoot,
        [string]$StartMenuRegistryRoot
    )

    $wixArgs = @(
        (Join-Path $installDir "FoToolbox.wxs"),
        (Join-Path $installDir "FoToolboxFiles.wxs"),
        "-d", "SourceDir=$SourceDir",
        "-d", "InstallScope=$Scope",
        "-d", "InstallRoot=$InstallRoot",
        "-d", "StartMenuRoot=$StartMenuRoot",
        "-d", "StartMenuRegistryRoot=$StartMenuRegistryRoot",
        "-d", "ProductCode=$ProductCodeValue"
    )
if (-not [string]::IsNullOrWhiteSpace($ProductName)) { $wixArgs += @("-d", "ProductName=$ProductName") }
    if (-not [string]::IsNullOrWhiteSpace($Manufacturer)) { $wixArgs += @("-d", "Manufacturer=$Manufacturer") }
    if (-not [string]::IsNullOrWhiteSpace($msiVersion)) { $wixArgs += @("-d", "Version=$msiVersion") }
    if (-not [string]::IsNullOrWhiteSpace($UpgradeCode)) { $wixArgs += @("-d", "UpgradeCode=$UpgradeCode") }
    if ($Configuration -eq "Debug") { $wixArgs += @("-d", "AllowDowngrades=yes") }
    if (-not [string]::IsNullOrWhiteSpace($ProductCodeUser)) { $wixArgs += @("-d", "ProductCodeUser=$ProductCodeUser") }
    if (-not [string]::IsNullOrWhiteSpace($ProductCodeMachine)) { $wixArgs += @("-d", "ProductCodeMachine=$ProductCodeMachine") }

    $wixArgs += @("-o", $OutputPath)
    wix build @wixArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "wix build failed for $OutputPath (exit code $LASTEXITCODE)."
    }
}

Write-Host "`nBuilding user MSI..."
Build-Msi -OutputPath $UserMsiPath -Scope "perUser" -ProductCodeValue $ProductCodeUser -InstallRoot "LocalAppDataFolder" -StartMenuRoot "ProgramMenuFolder" -StartMenuRegistryRoot "HKCU"
Sign-Output -FilePath $UserMsiPath -Description "FOtoolbox user installer"

Write-Host "`nBuilding machine MSI..."
Build-Msi -OutputPath $MachineMsiPath -Scope "perMachine" -ProductCodeValue $ProductCodeMachine -InstallRoot "ProgramFiles64Folder" -StartMenuRoot "ProgramMenuFolder" -StartMenuRegistryRoot "HKLM"
Sign-Output -FilePath $MachineMsiPath -Description "FOtoolbox machine installer"

if (-not (Test-Path $RuntimeExe)) {
    throw "Runtime installer not found at $RuntimeExe. Download the .NET Desktop Runtime and place it at that path, or pass -RuntimeExe to this script."
}

$wixExtensionRoot = Join-Path $HOME ".wix\extensions"
$balExtensionDll = Get-ChildItem (Join-Path $wixExtensionRoot "WixToolset.Bal.wixext") -Recurse -Filter "WixToolset.BootstrapperApplications.wixext.dll" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$utilExtensionDll = Get-ChildItem (Join-Path $wixExtensionRoot "WixToolset.Util.wixext") -Recurse -Filter "WixToolset.Util.wixext.dll" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if ([string]::IsNullOrWhiteSpace($balExtensionDll) -or [string]::IsNullOrWhiteSpace($utilExtensionDll)) {
    throw "Required WiX bundle extensions were not found in $wixExtensionRoot. Install WixToolset.Bal.wixext and WixToolset.Util.wixext for WiX 6."
}

Write-Host "`nComputing runtime payload hash and size for download verification..."
$runtimeFileInfo = Get-Item -LiteralPath $RuntimeExe
$runtimeSize = $runtimeFileInfo.Length
$runtimeHash = (Get-FileHash -Algorithm SHA512 -LiteralPath $RuntimeExe).Hash
Write-Host (" - RuntimeExe: {0}" -f $RuntimeExe)
Write-Host (" - Size: {0} bytes" -f $runtimeSize)
Write-Host (" - SHA-512: {0}" -f $runtimeHash)

Write-Host "`nBuilding bundle..."
$bundleArgs = @(
    (Join-Path $installDir "Bundle.wxs"),
    "-d", "FoToolboxUserMsiPath=$UserMsiPath",
    "-d", "FoToolboxMachineMsiPath=$MachineMsiPath",
    "-d", "NetDesktopRuntimeExe=$RuntimeExe",
    "-d", "NetDesktopRuntimeHash=$runtimeHash",
    "-d", "NetDesktopRuntimeSize=$runtimeSize"
)
if (-not [string]::IsNullOrWhiteSpace($RuntimeVersion)) { $bundleArgs += @("-d", "NetDesktopRuntimeVersion=$RuntimeVersion") }
if (-not [string]::IsNullOrWhiteSpace($BundleName)) { $bundleArgs += @("-d", "BundleName=$BundleName") }
if (-not [string]::IsNullOrWhiteSpace($BundleVersion)) { $bundleArgs += @("-d", "BundleVersion=$BundleVersion") }
if (-not [string]::IsNullOrWhiteSpace($BundleManufacturer)) { $bundleArgs += @("-d", "BundleManufacturer=$BundleManufacturer") }
if (-not [string]::IsNullOrWhiteSpace($BundleUpgradeCode)) { $bundleArgs += @("-d", "BundleUpgradeCode=$BundleUpgradeCode") }
if (-not [string]::IsNullOrWhiteSpace($BundleId)) { $bundleArgs += @("-d", "BundleId=$BundleId") }
if (-not [string]::IsNullOrWhiteSpace($LicenseUrl)) { $bundleArgs += @("-d", "LicenseUrl=$LicenseUrl") }

$bundleArgs += @(
    "-bindpath", $installDir,
    "-o", $BundlePath,
    "-ext", $balExtensionDll,
    "-ext", $utilExtensionDll
)

wix build @bundleArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "wix build failed for bundle $BundlePath (exit code $LASTEXITCODE)."
}
Sign-BurnBundle -BundlePath $BundlePath -Description "FOtoolbox bootstrapper bundle"

Write-SignedArtifactsSummary

Write-Host "`nDone."
