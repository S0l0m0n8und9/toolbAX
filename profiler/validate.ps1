# Dual-write Profiler Validation Script
# Ensures proper environment setup and runs validation tests

param(
  [string]$TestEnvUrl = "https://test.crm.dynamics.com",
  [string]$TestTenant = "test-tenant"
)

Write-Host "=== Dual-write Profiler Validation ===" -ForegroundColor Green
Write-Host "Test Environment URL: $TestEnvUrl"
Write-Host "Test Tenant: $TestTenant`n"

# Set environment variables for the validation
$env:TEST_ENV_URL = $TestEnvUrl
$env:TEST_TENANT = $TestTenant

# Verify build artifact exists
$distDir = Join-Path $PSScriptRoot "dist"
$indexJs = Join-Path $distDir "index.js"

if (-not (Test-Path $indexJs)) {
  Write-Host "Error: Build artifact not found at $indexJs" -ForegroundColor Red
  Write-Host "Run: npm run build" -ForegroundColor Yellow
  exit 1
}

Write-Host "✓ Build artifact found"

# Run the smoke test suite
Write-Host "Running smoke test suite...`n"

$smokeTest = Join-Path $PSScriptRoot "smoke-test.js"
& node $smokeTest

if ($LASTEXITCODE -ne 0) {
  Write-Host "`n✗ Validation failed" -ForegroundColor Red
  exit 1
}

Write-Host "`n✓ All validation checks passed!" -ForegroundColor Green
exit 0
