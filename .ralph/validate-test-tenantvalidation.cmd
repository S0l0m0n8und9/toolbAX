@echo off
setlocal
pushd "%~dp0.."
dotnet test .\FoToolbox.sln -c Release --filter TenantValidation
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
