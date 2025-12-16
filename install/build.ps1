param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$SourceDir = "",
    [string]$MsiPath = "",
    [string]$BundlePath = "",
    [string]$RuntimeExe = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installDir = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $installDir "..") | Select-Object -ExpandProperty Path

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot "artifacts\\FoToolbox"
}
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $installDir "FoToolbox.msi"
}
if ([string]::IsNullOrWhiteSpace($BundlePath)) {
    $BundlePath = Join-Path $installDir "FoToolboxBundle.exe"
}
if ([string]::IsNullOrWhiteSpace($RuntimeExe)) {
    $RuntimeExe = Join-Path $installDir "redist\\windowsdesktop-runtime-8.0.22-win-x64.exe"
}

Write-Host "Repo root: $repoRoot"
Write-Host "Configuration: $Configuration"
Write-Host "SourceDir: $SourceDir"
Write-Host "MSI: $MsiPath"
Write-Host "Bundle: $BundlePath"
Write-Host "RuntimeExe: $RuntimeExe"

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
    -o $SourceDir | Out-Host

$pluginsOut = Join-Path $SourceDir "plugins"
New-Item -ItemType Directory -Force -Path $pluginsOut | Out-Null

Write-Host "`nBuilding plugins..."
dotnet build (Join-Path $repoRoot "plugins\\HelloPlugin\\HelloPlugin.csproj") -c $Configuration | Out-Host
dotnet build (Join-Path $repoRoot "plugins\\QueryBuilder\\QueryBuilder.csproj") -c $Configuration | Out-Host

Write-Host "`nCopying plugin binaries to SourceDir..."
Copy-Item (Join-Path $repoRoot "plugins\\HelloPlugin\\bin\\$Configuration\\net8.0-windows\\HelloPlugin.dll") `
    -Destination (Join-Path $pluginsOut "HelloPlugin.dll") -Force
Copy-Item (Join-Path $repoRoot "plugins\\QueryBuilder\\bin\\$Configuration\\net8.0-windows\\QueryBuilder.dll") `
    -Destination (Join-Path $pluginsOut "QueryBuilder.dll") -Force

Write-Host "`nBuilding MSI..."
wix build (Join-Path $installDir "FoToolbox.wxs") (Join-Path $installDir "FoToolboxFiles.wxs") `
    -d SourceDir=$SourceDir `
    -o $MsiPath | Out-Host

if (-not (Test-Path $RuntimeExe)) {
    Write-Warning "Runtime installer not found at $RuntimeExe. Skipping bundle build."
    Write-Host "Download the .NET Desktop Runtime and place it at that path, or pass -RuntimeExe to this script."
    exit 0
}

Write-Host "`nBuilding bundle..."
wix build (Join-Path $installDir "Bundle.wxs") `
    -d FoToolboxMsiPath=$MsiPath `
    -d NetDesktopRuntimeExe=$RuntimeExe `
    -o $BundlePath `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Util.wixext | Out-Host

Write-Host "`nDone."

