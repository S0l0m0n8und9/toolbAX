@echo off
setlocal
pushd "%~dp0.."
dotnet test .\FoToolbox.sln -c Release --filter EnumCoverage
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
