param(
    [int]$Port = 8787,
    [string]$Root = "."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$rootPath = Resolve-Path $Root | Select-Object -ExpandProperty Path
$prefix = "http://localhost:$Port/"

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)
$listener.Start()

Write-Host "Serving $rootPath at $prefix (Ctrl+C to stop)"

function Get-ContentType {
    param([string]$Path)
    switch ([IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        ".json" { return "application/json" }
        ".msi" { return "application/octet-stream" }
        ".exe" { return "application/octet-stream" }
        ".txt" { return "text/plain" }
        default { return "application/octet-stream" }
    }
}

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $path = $context.Request.Url.AbsolutePath.TrimStart("/")
        if ([string]::IsNullOrWhiteSpace($path)) {
            $context.Response.StatusCode = 404
            $context.Response.Close()
            continue
        }

        $filePath = Join-Path $rootPath $path
        if (-not (Test-Path $filePath)) {
            $context.Response.StatusCode = 404
            $context.Response.Close()
            continue
        }

        $bytes = [IO.File]::ReadAllBytes($filePath)
        $context.Response.ContentType = Get-ContentType -Path $filePath
        $context.Response.ContentLength64 = $bytes.Length
        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $context.Response.OutputStream.Close()
    }
    catch {
        try { $context.Response.StatusCode = 500; $context.Response.Close() } catch { }
    }
}
