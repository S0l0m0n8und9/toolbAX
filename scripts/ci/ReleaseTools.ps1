Set-StrictMode -Version Latest

function Convert-ReleaseTag {
    param([string]$Tag)
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = '\Av(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>' + $identifier + '(?:\.' + $identifier + ')*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z'
    $match = [regex]::Match($Tag, $pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) { throw 'Tag must be a safe vMAJOR.MINOR.PATCH semantic version.' }
    foreach ($part in @('major', 'minor', 'patch')) {
        $number = 0
        if (-not [int]::TryParse($match.Groups[$part].Value, [ref]$number) -or $number -gt 65534) { throw 'Version component exceeds the numeric assembly/file-version range.' }
    }
    [pscustomobject]@{ Tag = $Tag; PackageVersion = $Tag.Substring(1)
        IsPrerelease = $match.Groups['major'].Value -eq '0' -or $match.Groups['pre'].Success
        FileVersion = '{0}.{1}.{2}.0' -f $match.Groups['major'].Value, $match.Groups['minor'].Value, $match.Groups['patch'].Value }
}

function Assert-BuildInputs {
    param([string]$SourceSha, [string]$PackageVersion, [string]$FileVersion)
    if ($SourceSha -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Source must be one full immutable lowercase commit SHA.' }
    $version = Convert-ReleaseTag ('v' + $PackageVersion)
    if ($FileVersion -cne $version.FileVersion) { throw 'FileVersion does not match the validated numeric version.' }
}

function Assert-PackageAuditJson {
    param([string]$Json, [int]$ExitCode)
    if ($ExitCode -ne 0) { throw "Package audit command failed ($ExitCode)." }
    $report = ConvertFrom-Json -InputObject $Json -AsHashtable -Depth 100 -ErrorAction Stop
    if ($report -isnot [System.Collections.IDictionary] -or $report['version'] -ne 1 -or
        $report['sources'] -isnot [System.Collections.IList] -or $report['sources'].Count -eq 0 -or
        $report['projects'] -isnot [System.Collections.IList] -or $report['projects'].Count -eq 0 -or
        $report['parameters'] -isnot [string] -or $report['parameters'] -notmatch '--vulnerable' -or
        $report['parameters'] -notmatch '--include-transitive') { throw 'Missing or incomplete package audit output.' }
    foreach ($source in $report['sources']) {
        if ($source -isnot [string] -or [string]::IsNullOrWhiteSpace($source)) { throw 'Malformed audit source.' }
    }
    foreach ($project in $report['projects']) {
        if ($project -isnot [System.Collections.IDictionary] -or $project['path'] -isnot [string] -or
            [string]::IsNullOrWhiteSpace($project['path'])) { throw 'Malformed or missing audited project.' }
        # A clean --vulnerable report legitimately contains only a project path. If frameworks is
        # present, however, a null/malformed collection must not masquerade as that clean omission.
        if ($project.Contains('frameworks')) {
            if ($project['frameworks'] -isnot [System.Collections.IList] -or $project['frameworks'].Count -eq 0) { throw 'Malformed audited frameworks.' }
            foreach ($framework in $project['frameworks']) {
                if ($framework -isnot [System.Collections.IDictionary] -or $framework['framework'] -isnot [string] -or
                    [string]::IsNullOrWhiteSpace($framework['framework'])) { throw 'Malformed audited framework.' }
            }
        }
    }
    function Inspect-AuditNode($node) {
        if ($node -is [System.Collections.IDictionary]) {
            foreach ($key in $node.Keys) {
                if ($key -in @('vulnerabilities', 'errors', 'problems') -and @($node[$key]).Count -gt 0) { throw "Package audit reported $key." }
                if ($key -in @('level', 'severity') -and $node[$key] -in @('Error', 'Warning')) { throw 'Package audit reported a diagnostic failure.' }
                Inspect-AuditNode $node[$key]
            }
        } elseif ($node -is [System.Collections.IList]) { foreach ($item in $node) { Inspect-AuditNode $item } }
    }
    Inspect-AuditNode $report
}

function Assert-SmokeReport {
    param([string]$ReportPath, [string]$DataDirectory, [string]$Executable, [string]$SourceSha,
        [string]$PackageVersion, [string]$FileVersion, [int]$ProcessExitCode)
    Assert-BuildInputs $SourceSha $PackageVersion $FileVersion
    if ($ProcessExitCode -ne 0) { throw "Packaged app smoke exited $ProcessExitCode. Inspect its report/stdout/stderr." }
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    if (($report['schemaVersion'] -isnot [long] -and $report['schemaVersion'] -isnot [int]) -or
        $report['schemaVersion'] -ne 1 -or $report['success'] -isnot [bool] -or $report['success'] -ne $true -or
        $report['exitCode'] -ne 0 -or $report['failures'] -isnot [array] -or @($report['failures']).Count -ne 0) { throw 'Smoke report is missing, unsuccessful or has an unsupported schema.' }
    if ([IO.Path]::GetFullPath($report['dataDirectory']) -cne [IO.Path]::GetFullPath($DataDirectory) -or
        [IO.Path]::GetFullPath($report['executable']) -cne [IO.Path]::GetFullPath($Executable)) { throw 'Smoke report has different executable/data provenance.' }
    $o = $report['observation']
    foreach ($property in @('windows', 'mainWindowReady', 'homeReady', 'realComposition')) {
        if ($o[$property] -isnot [bool] -or $o[$property] -ne $true) { throw "Smoke readiness failed: $property." }
    }
    if ($o['degraded'] -isnot [bool] -or $o['hasActiveEnvironment'] -isnot [bool] -or
        $o['degraded'] -ne $false -or $o['profileCount'] -ne 0 -or $o['hasActiveEnvironment'] -ne $false -or
        [IO.Path]::GetFullPath($o['profileDbPath']) -cne (Join-Path ([IO.Path]::GetFullPath($DataDirectory)) 'profile.db')) { throw 'Smoke did not use empty isolated real composition.' }
    foreach ($tool in @('home','profiles','query','post','metadata','ops','compare','mapbrowser','virtualtables')) {
        if ($tool -cnotin $o['tools']) { throw "Smoke is missing tool $tool." }
    }
    $information = if ($PackageVersion.Contains('+')) { "$PackageVersion.$SourceSha" } else { "$PackageVersion+$SourceSha" }
    if (@($o['assemblies']).Count -ne 3) { throw 'Smoke assembly metadata is incomplete.' }
    foreach ($name in @('toolbAX', 'FoToolbox.Core', 'toolBax.Core')) {
        $assemblies = @($o['assemblies'] | Where-Object { $_['name'] -ceq $name })
        if ($assemblies.Count -ne 1 -or $assemblies[0]['configuration'] -cne 'Release' -or
            $assemblies[0]['informationalVersion'] -cne $information -or $assemblies[0]['fileVersion'] -cne $FileVersion) { throw "Wrong runtime Release/version/SHA metadata: $name." }
    }
    if ($o['webView2']['compiled'] -isnot [bool] -or $o['webView2']['compiled'] -ne $true -or $o['webView2']['status'] -cne 'available' -or
        [string]::IsNullOrWhiteSpace($o['webView2']['version'])) { throw 'WebView2 loader/runtime capability unavailable.' }
    return $report
}

function Assert-ReleaseSourceEligibility {
    param([string[]]$Paths)
    $required = @(
        '.github/workflows/ci.yml',
        'scripts/ci/ReleaseTools.ps1', 'scripts/ci/Test-ReleaseTools.ps1',
        'scripts/ci/Assert-PackageAudit.ps1', 'scripts/ci/Build-SmokePackage.ps1',
        'scripts/ci/Invoke-PackageSmoke.ps1', 'scripts/ci/Resolve-ReleaseTag.ps1',
        'avalonia/toolBax.App/StartupSmoke.cs', 'avalonia/toolBax.App/Program.cs',
        'avalonia/toolBax.App/App.axaml.cs'
    )
    $missing = @($required | Where-Object { $Paths -cnotcontains $_ })
    if ($missing.Count -gt 0) {
        throw ('Selected tag predates or lacks the required release verification contract: ' +
            ($missing -join ', ') + '. Select a tag whose source includes the CI helpers and startup smoke entry point. ' +
            'Historical tags/releases are unchanged; this workflow cannot retroactively verify them or inject newer app code.')
    }
}
