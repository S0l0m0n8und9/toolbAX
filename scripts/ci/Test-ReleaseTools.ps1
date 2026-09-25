$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseTools.ps1"
$checks = 0
$failures = [System.Collections.Generic.List[string]]::new()
function Check([string]$Name, [scriptblock]$Body) {
    $script:checks++
    try { & $Body; Write-Output "PASS $Name" } catch { $script:failures.Add("$Name : $($_.Exception.Message)"); Write-Output "FAIL $Name" }
}
function Reject([scriptblock]$Body) {
    $rejected = $false
    try { & $Body | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected rejection.' }
}
Check 'stable version gets numeric file version' {
    $v = Convert-ReleaseTag 'v1.2.3'
    if ($v.PackageVersion -ne '1.2.3' -or $v.FileVersion -ne '1.2.3.0') { throw 'Version mismatch.' }
}
Check 'prerelease and build metadata never enter FileVersion' {
    $v = Convert-ReleaseTag 'v12.34.56-rc.2+build.7'
    if ($v.PackageVersion -ne '12.34.56-rc.2+build.7' -or $v.FileVersion -ne '12.34.56.0') { throw 'Unsafe numeric version.' }
}
foreach ($case in @(
    @{ Tag = 'v1.2.3+build-7'; Expected = $false },
    @{ Tag = 'v1.2.3-rc.1+build-7'; Expected = $true },
    @{ Tag = 'v0.1.0'; Expected = $true }
)) {
    Check "validated prerelease label for $($case.Tag)" {
        $version = Convert-ReleaseTag $case.Tag
        if ($version.IsPrerelease -isnot [bool] -or $version.IsPrerelease -ne $case.Expected) {
            throw "Incorrect prerelease decision for $($case.Tag)."
        }
    }
}
foreach ($tag in @('1.2.3', 'vv1.2.3', 'v01.2.3', 'v1.2', 'v1.2.3-01', 'v70000.2.3', 'v1.2.3;Write-Output bad', 'v1.2.3$(whoami)', "v1.2.3`nversion=bad")) {
    Check "reject tag [$tag]" { Reject { Convert-ReleaseTag $tag } }
}
Check 'immutable build input accepts matching versions' { Assert-BuildInputs ('a' * 40) '1.2.3-beta+build' '1.2.3.0' }
Check 'mutable source ref is rejected' { Reject { Assert-BuildInputs 'main' '1.2.3' '1.2.3.0' } }
Check 'mismatched numeric version is rejected' { Reject { Assert-BuildInputs ('a' * 40) '1.2.3' '4.5.6.0' } }
$clean = '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"App.csproj","frameworks":[{"framework":"net10.0"}]}]}'
Check 'completed clean audit accepted' { Assert-PackageAuditJson $clean 0 }
$pathOnly = '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"App.csproj"}]}'
Check 'genuine clean path-only project audit accepted' { Assert-PackageAuditJson $pathOnly 0 }
foreach ($collection in @('null', '{}', '[null]', '[{}]', '[{"path":null}]', '[{"path":" "}]')) {
    $badProjects = '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":' + $collection + '}'
    Check "reject malformed project collection $collection" { Reject { Assert-PackageAuditJson $badProjects 0 } }
}
Check 'missing project collection rejected' { Reject { Assert-PackageAuditJson '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"]}' 0 } }
foreach ($collection in @('null', '{}', '[]', '[null]', '[" "]')) {
    $badSources = '{"version":1,"parameters":"--vulnerable --include-transitive","sources":' + $collection + ',"projects":[{"path":"App.csproj"}]}'
    Check "reject malformed audit sources $collection" { Reject { Assert-PackageAuditJson $badSources 0 } }
}
Check 'explicit null frameworks is not clean omission' { Reject { Assert-PackageAuditJson ($pathOnly.Replace('"path":"App.csproj"', '"path":"App.csproj","frameworks":null')) 0 } }
Check 'nonzero audit exit rejected even with clean JSON' { Reject { Assert-PackageAuditJson $clean 1 } }
Check 'malformed audit rejected' { Reject { Assert-PackageAuditJson 'not JSON' 0 } }
Check 'empty audit rejected' { Reject { Assert-PackageAuditJson '{"version":1,"projects":[]}' 0 } }
Check 'audit warning cannot silently pass' { Reject { Assert-PackageAuditJson ($clean.Replace('"projects":', '"problems":[{"severity":"Warning","text":"NU1900"}],"projects":')) 0 } }
Check 'transitive vulnerability detected from structured output' {
    Reject { Assert-PackageAuditJson ($clean.Replace('"framework":"net10.0"', '"framework":"net10.0","transitivePackages":[{"id":"Bad","vulnerabilities":[{"severity":"High"}]}]')) 0 }
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('toolbax-release-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    $data = Join-Path $fixture 'data'
    $exe = Join-Path $fixture 'extracted/toolbAX.exe'
    $reportPath = Join-Path $fixture 'report.json'
    $sha = 'a' * 40
    $package = '1.2.3-beta+build'
    $report = @{ schemaVersion = 1; success = $true; exitCode = 0; dataDirectory = $data; executable = $exe; failures = @()
        observation = @{ windows = $true; mainWindowReady = $true; homeReady = $true; realComposition = $true
            degraded = $false; profileCount = 0; hasActiveEnvironment = $false; profileDbPath = (Join-Path $data 'profile.db')
            tools = @('home','profiles','query','post','metadata','ops','compare','mapbrowser','virtualtables')
            assemblies = @('toolbAX','FoToolbox.Core','toolBax.Core') | ForEach-Object { @{ name = $_; configuration = 'Release'; informationalVersion = "$package.$sha"; fileVersion = '1.2.3.0' } }
            webView2 = @{ compiled = $true; status = 'available'; version = '142.0.0.0 beta' } } }
    $originalJson = $report | ConvertTo-Json -Depth 10
    $originalJson | Set-Content -LiteralPath $reportPath
    Check 'complete smoke provenance accepted with channel reported honestly' { Assert-SmokeReport $reportPath $data $exe $sha $package '1.2.3.0' 0 | Out-Null }
    Check 'nonzero process cannot pass a success report' { Reject { Assert-SmokeReport $reportPath $data $exe $sha $package '1.2.3.0' 7 } }
    foreach ($case in @('wrongSha','debugCore','fake','wrongData','missingRuntime','wrongSchema','missingHome')) {
        $copy = $originalJson | ConvertFrom-Json -AsHashtable -Depth 10
        switch ($case) {
            'wrongSha' { $copy.observation.assemblies[0].informationalVersion = '1.2.3-beta+build.' + ('b' * 40) }
            'debugCore' { $copy.observation.assemblies[1].configuration = 'Debug' }
            'fake' { $copy.observation.realComposition = $false }
            'wrongData' { $copy.dataDirectory = Join-Path $fixture 'unexpected' }
            'missingRuntime' { $copy.observation.webView2.status = 'missing-runtime'; $copy.observation.webView2.version = $null }
            'wrongSchema' { $copy.schemaVersion = '1' }
            'missingHome' { $copy.observation.homeReady = $false }
        }
        $copy | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath
        Check "reject smoke $case" { Reject { Assert-SmokeReport $reportPath $data $exe $sha $package '1.2.3.0' 0 } }
    }
    Check 'missing report rejected' { Reject { Assert-SmokeReport (Join-Path $fixture 'missing.json') $data $exe $sha $package '1.2.3.0' 0 } }
} finally {
    # Only the unique fixture created above; never a report-provided path.
    $resolved = [IO.Path]::GetFullPath($fixture)
    $expectedPrefix = Join-Path ([IO.Path]::GetTempPath()) 'toolbax-release-check-'
    if (-not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output "$checks checks; $($failures.Count) failed."
if ($failures.Count) { $failures | Write-Output; exit 1 }
exit 0
