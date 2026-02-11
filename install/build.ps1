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
    [string]$LicenseUrl = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
    $RuntimeExe = Join-Path $installDir "redist\\windowsdesktop-runtime-8.0.22-win-x64.exe"
}

if ([string]::IsNullOrWhiteSpace($ProductCodeUser)) {
    # Default to auto-generated ProductCode so each build can upgrade in-place (no uninstall needed).
    $ProductCodeUser = if (-not [string]::IsNullOrWhiteSpace($ProductCode)) { $ProductCode } else { "*" }
}
if ([string]::IsNullOrWhiteSpace($ProductCodeMachine)) {
    $ProductCodeMachine = "*"
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
if (-not [string]::IsNullOrWhiteSpace($LicenseUrl)) { Write-Host "LicenseUrl: $LicenseUrl" }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required but was not found on PATH."
}
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix is required but was not found on PATH. Install with: dotnet tool install --global wix"
}

New-Item -ItemType Directory -Force -Path $SourceDir | Out-Null

Write-Host "`nPublishing host..."
dotnet publish (Join-Path $repoRoot "src\\FoToolbox.Host\\FoToolbox.Host.csproj") `
    -c $Configuration `
    -o $SourceDir `
    -p:Version=$msiVersion `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$fileVersion | Out-Host

$pluginsOut = Join-Path $SourceDir "plugins"
New-Item -ItemType Directory -Force -Path $pluginsOut | Out-Null

Write-Host "`nBuilding plugins..."
dotnet build (Join-Path $repoRoot "plugins\\HelloPlugin\\HelloPlugin.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\QueryBuilder\\QueryBuilder.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\TableEntityBrowser\\TableEntityBrowser.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\ODataPostBuilder\\ODataPostBuilder.csproj") -c $Configuration -p:Version=$msiVersion -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion | Out-Host

Write-Host "`nCopying plugin binaries to SourceDir..."
Copy-Item (Join-Path $repoRoot "plugins\\HelloPlugin\\bin\\$Configuration\\net8.0-windows\\HelloPlugin.dll") `
    -Destination (Join-Path $pluginsOut "HelloPlugin.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\QueryBuilder\\bin\\$Configuration\\net8.0-windows\\QueryBuilder.dll") `
    -Destination (Join-Path $pluginsOut "QueryBuilder.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\TableEntityBrowser\\bin\\$Configuration\\net8.0-windows\\TableEntityBrowser.dll") `
    -Destination (Join-Path $pluginsOut "TableEntityBrowser.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\ODataPostBuilder\\bin\\$Configuration\\net8.0-windows\\ODataPostBuilder.dll") `
    -Destination (Join-Path $pluginsOut "ODataPostBuilder.dll") -Force

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

    $wixArgs += @("-o", $OutputPath)
    wix build @wixArgs | Out-Host
}

Write-Host "`nBuilding user MSI..."
Build-Msi -OutputPath $UserMsiPath -Scope "perUser" -ProductCodeValue $ProductCodeUser -InstallRoot "LocalAppDataFolder" -StartMenuRoot "ProgramMenuFolder" -StartMenuRegistryRoot "HKCU"

Write-Host "`nBuilding machine MSI..."
Build-Msi -OutputPath $MachineMsiPath -Scope "perMachine" -ProductCodeValue $ProductCodeMachine -InstallRoot "ProgramFiles64Folder" -StartMenuRoot "ProgramMenuFolder" -StartMenuRegistryRoot "HKLM"

if (-not (Test-Path $RuntimeExe)) {
    Write-Warning "Runtime installer not found at $RuntimeExe. Skipping bundle build."
    Write-Host "Download the .NET Desktop Runtime and place it at that path, or pass -RuntimeExe to this script."
    exit 0
}

Write-Host "`nBuilding bundle..."
$bundleArgs = @(
    (Join-Path $installDir "Bundle.wxs"),
    "-d", "FoToolboxUserMsiPath=$UserMsiPath",
    "-d", "FoToolboxMachineMsiPath=$MachineMsiPath",
    "-d", "NetDesktopRuntimeExe=$RuntimeExe"
)
if (-not [string]::IsNullOrWhiteSpace($RuntimeVersion)) { $bundleArgs += @("-d", "NetDesktopRuntimeVersion=$RuntimeVersion") }
if (-not [string]::IsNullOrWhiteSpace($BundleName)) { $bundleArgs += @("-d", "BundleName=$BundleName") }
if (-not [string]::IsNullOrWhiteSpace($BundleVersion)) { $bundleArgs += @("-d", "BundleVersion=$BundleVersion") }
if (-not [string]::IsNullOrWhiteSpace($BundleManufacturer)) { $bundleArgs += @("-d", "BundleManufacturer=$BundleManufacturer") }
if (-not [string]::IsNullOrWhiteSpace($BundleUpgradeCode)) { $bundleArgs += @("-d", "BundleUpgradeCode=$BundleUpgradeCode") }
if (-not [string]::IsNullOrWhiteSpace($LicenseUrl)) { $bundleArgs += @("-d", "LicenseUrl=$LicenseUrl") }

$bundleArgs += @(
    "-o", $BundlePath,
    "-ext", "WixToolset.BootstrapperApplications.wixext",
    "-ext", "WixToolset.Util.wixext"
)

wix build @bundleArgs | Out-Host

Write-Host "`nDone."
