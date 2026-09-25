param([string]$Project = 'avalonia/toolBax.App/toolBax.App.csproj', [string]$OutputPath = 'artifacts/package-audit.json')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseTools.ps1"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
$errorsPath = "$OutputPath.stderr"
& dotnet list $Project package --vulnerable --include-transitive --no-restore --format json --output-version 1 --source https://api.nuget.org/v3/index.json 1> $OutputPath 2> $errorsPath
$code = $LASTEXITCODE
if ((Get-Item -LiteralPath $errorsPath).Length -gt 0) { throw "Package audit wrote diagnostics; see $errorsPath." }
Assert-PackageAuditJson (Get-Content -LiteralPath $OutputPath -Raw) $code
Write-Output 'Shipping dependency audit completed with no reported vulnerabilities.'
