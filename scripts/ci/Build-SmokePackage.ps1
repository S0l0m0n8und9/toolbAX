param([Parameter(Mandatory)][string]$SourceSha, [string]$PackageVersion = '0.0.0-ci',
    [string]$FileVersion = '0.0.0.0', [string]$OutputDirectory = 'artifacts/package')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseTools.ps1"
Assert-BuildInputs $SourceSha $PackageVersion $FileVersion
if (-not $IsWindows) { throw 'The shipping package is built and smoked on Windows.' }
$head = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head.Trim() -cne $SourceSha) { throw 'Checkout is not the immutable verified source.' }
& git diff --quiet HEAD -- .
if ($LASTEXITCODE -ne 0) { throw 'Tracked source must be committed before packaging provenance can be claimed.' }
$untracked = @(& git ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not verify untracked source state.' }
if ($untracked.Count -ne 0) { throw 'Untracked source files must be committed or excluded before packaging.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Package output directory must be absent or empty.' }
} else { New-Item -ItemType Directory -Path $output | Out-Null }
$publish = Join-Path $output 'publish'
$env:CI = 'true'
& dotnet publish avalonia/toolBax.App/toolBax.App.csproj -c Release -r win-x64 --self-contained true `
    -p:EnableWebView2=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:Version=$PackageVersion" "-p:InformationalVersion=$PackageVersion" "-p:FileVersion=$FileVersion" `
    "-p:AssemblyVersion=$FileVersion" "-p:SourceRevisionId=$SourceSha" -p:IncludeSourceRevisionInInformationalVersion=true `
    -o $publish 2>&1 | Tee-Object -FilePath (Join-Path $output 'publish.log')
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $publish 'toolbAX.exe'))) { throw 'Publish did not produce toolbAX.exe.' }
$zip = Join-Path $output 'toolbAX-win-x64.zip'
$stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = "$zip.sha256"
[IO.File]::WriteAllText($checksum, "$hash  toolbAX-win-x64.zip`n", [Text.Encoding]::ASCII)
& "$PSScriptRoot/Invoke-PackageSmoke.ps1" -ZipPath $zip -ChecksumPath $checksum -SourceSha $SourceSha `
    -PackageVersion $PackageVersion -FileVersion $FileVersion -OutputDirectory $output
