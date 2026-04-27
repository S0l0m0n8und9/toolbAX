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
    [string]$SignCertificateThumbprint = "",
    [string]$SignCertificateFile = "",
    [string]$SignCertificatePassword = "",
    [string]$SignTimestampUrl = "http://timestamp.digicert.com",
    [string]$SignFileDigest = "sha256",
    [string]$SignTimestampDigest = "sha256"
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
$defaultProductCodeUser = "{F057FFFE-9295-4B8D-A60F-41CB15E1ABB6}"
$defaultProductCodeMachine = "{6F8A8A0D-7791-4B24-8A2F-D4E8E93FE4AA}"
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
    $RuntimeExe = Join-Path $installDir "redist\\windowsdesktop-runtime-8.0.22-win-x64.exe"
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
    if ($Configuration -ne "Release") {
        return
    }

    if (-not (Test-SigningRequested)) {
        throw "Signing is required for release installer outputs. Pass -SignCertificateThumbprint or -SignCertificateFile before running install/build.ps1 -Configuration Release."
    }

    if (-not [string]::IsNullOrWhiteSpace($SignCertificateFile) -and -not (Test-Path $SignCertificateFile)) {
        throw "Signing certificate file was not found at $SignCertificateFile."
    }

    $signTool = Get-SignToolPath -ExplicitPath $SignToolPath
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        throw "signtool.exe was not found. Install the Windows SDK or pass -SignToolPath before running install/build.ps1 -Configuration Release."
    }
}

function Sign-Output {
    param(
        [string]$FilePath,
        [string]$Description
    )

    if (-not (Test-SigningRequested)) {
        Write-Host "Skipping signing for $Description; no signing certificate was configured."
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

    if (-not [string]::IsNullOrWhiteSpace($SignCertificateFile)) {
        $signArgs += @("/f", $SignCertificateFile)
        if (-not [string]::IsNullOrWhiteSpace($SignCertificatePassword)) {
            $signArgs += @("/p", $SignCertificatePassword)
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
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required but was not found on PATH."
}
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix is required but was not found on PATH. Install with: dotnet tool install --global wix"
}

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
New-Item -ItemType Directory -Force -Path $pluginsOut | Out-Null

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

Write-Host "`nCopying plugin binaries to SourceDir..."
Copy-Item (Join-Path $repoRoot "plugins\\HelloPlugin\\bin\\$Configuration\\net8.0-windows\\HelloPlugin.dll") `
    -Destination (Join-Path $pluginsOut "HelloPlugin.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\QueryBuilder\\bin\\$Configuration\\net8.0-windows\\QueryBuilder.dll") `
    -Destination (Join-Path $pluginsOut "QueryBuilder.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\TableEntityBrowser\\bin\\$Configuration\\net8.0-windows\\TableEntityBrowser.dll") `
    -Destination (Join-Path $pluginsOut "TableEntityBrowser.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\ODataPostBuilder\\bin\\$Configuration\\net8.0-windows\\ODataPostBuilder.dll") `
    -Destination (Join-Path $pluginsOut "ODataPostBuilder.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\DualWriteMapBrowser\\bin\\$Configuration\\net8.0-windows\\DualWriteMapBrowser.dll") `
    -Destination (Join-Path $pluginsOut "DualWriteMapBrowser.dll") -Force

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
    if (-not [string]::IsNullOrWhiteSpace($ProductCodeUser)) { $wixArgs += @("-d", "ProductCodeUser=$ProductCodeUser") }
    if (-not [string]::IsNullOrWhiteSpace($ProductCodeMachine)) { $wixArgs += @("-d", "ProductCodeMachine=$ProductCodeMachine") }

    $wixArgs += @("-o", $OutputPath)
    wix build @wixArgs | Out-Host
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
if (-not [string]::IsNullOrWhiteSpace($BundleId)) { $bundleArgs += @("-d", "BundleId=$BundleId") }
if (-not [string]::IsNullOrWhiteSpace($LicenseUrl)) { $bundleArgs += @("-d", "LicenseUrl=$LicenseUrl") }

$bundleArgs += @(
    "-o", $BundlePath,
    "-ext", $balExtensionDll,
    "-ext", $utilExtensionDll
)

wix build @bundleArgs | Out-Host
Sign-Output -FilePath $BundlePath -Description "FOtoolbox bootstrapper bundle"

Write-Host "`nDone."
