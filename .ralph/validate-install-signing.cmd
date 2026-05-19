@echo off
setlocal
if "%TEST_CERT_THUMBPRINT%"=="" (
  echo TEST_CERT_THUMBPRINT must be set for installer signing validation.
  exit /b 1
)
pushd "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File .\install\build.ps1 -CertThumbprint "%TEST_CERT_THUMBPRINT%"
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
