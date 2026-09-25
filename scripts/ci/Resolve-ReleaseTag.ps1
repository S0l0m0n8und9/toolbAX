param([Parameter(Mandatory)][string]$Tag, [string]$RepositoryRoot = '.', [string]$ExpectedSha)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseTools.ps1"
$version = Convert-ReleaseTag $Tag
$sha = & git -C $RepositoryRoot rev-parse --verify "refs/tags/$Tag^{commit}" 2>$null
if ($LASTEXITCODE -ne 0 -or @($sha).Count -ne 1) { throw 'The validated tag does not exist or does not resolve to a commit.' }
$sha = $sha.Trim().ToLowerInvariant()
Assert-BuildInputs $sha $version.PackageVersion $version.FileVersion
if ($ExpectedSha -and $sha -cne $ExpectedSha) { throw 'The tag no longer points at the verified source SHA.' }
if ($env:GITHUB_OUTPUT) {
    @("source_sha=$sha", "package_version=$($version.PackageVersion)", "file_version=$($version.FileVersion)", "tag=$Tag", "is_prerelease=$($version.IsPrerelease.ToString().ToLowerInvariant())") |
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
[pscustomobject]@{ SourceSha = $sha; Tag = $Tag; PackageVersion = $version.PackageVersion; FileVersion = $version.FileVersion; IsPrerelease = $version.IsPrerelease } | ConvertTo-Json
