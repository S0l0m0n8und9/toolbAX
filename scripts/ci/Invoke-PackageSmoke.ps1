param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string]$ChecksumPath,
    [Parameter(Mandatory)][string]$SourceSha, [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$FileVersion, [string]$OutputDirectory = 'artifacts/package',
    [ValidateRange(5,180)][int]$TimeoutSeconds = 60)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseTools.ps1"
Assert-BuildInputs $SourceSha $PackageVersion $FileVersion
if (-not $IsWindows) { throw 'Native packaged smoke requires Windows.' }
$sum = (Get-Content -LiteralPath $ChecksumPath -Raw).Trim()
if ($sum -cnotmatch '\A([0-9a-f]{64})  toolbAX-win-x64\.zip\z') { throw 'Invalid package checksum file.' }
$expectedHash = $Matches[1]
if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedHash) { throw 'Package checksum mismatch.' }
$parent = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($parent) | Out-Null
$scratch = Join-Path $parent ('smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
$extracted = Join-Path $scratch 'extracted'
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ZipPath), $extracted)
$executable = Join-Path $extracted 'toolbAX.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The extracted package lacks toolbAX.exe.' }
$data = Join-Path $scratch 'data'
$report = Join-Path $scratch 'report.json'
$info = [Diagnostics.ProcessStartInfo]::new($executable)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.WorkingDirectory = $extracted
foreach ($argument in @('--smoke-test','--smoke-data-dir',$data,'--smoke-report',$report)) { $info.ArgumentList.Add($argument) }
$info.Environment['FOTOOLBOX_APPDATA_DIR'] = $data
$info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $scratch 'bundle'
$process = [Diagnostics.Process]::new()
$process.StartInfo = $info
$started = $false
try {
    if (-not $process.Start()) { throw 'Packaged app process did not start.' }
    $started = $true
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $exited = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        # Only this owned process/tree; no enumeration or termination of unrelated applications.
        $process.Kill($true)
        if (-not $process.WaitForExit(10000)) { throw 'Owned smoke process did not terminate after timeout.' }
    }
    [IO.File]::WriteAllText((Join-Path $scratch 'stdout.log'), $stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $scratch 'stderr.log'), $stderr.GetAwaiter().GetResult())
    if (-not $exited) { throw "Packaged startup timed out; evidence retained at $scratch." }
    $result = Assert-SmokeReport $report $data $executable $SourceSha $PackageVersion $FileVersion $process.ExitCode
    Write-Output "Native extracted-package smoke passed; WebView2 reports $($result.observation.webView2.version). Evidence: $scratch"
    return $report
} finally {
    if ($started -and -not $process.HasExited) {
        $process.Kill($true)
        $null = $process.WaitForExit(10000)
    }
    $process.Dispose()
}
