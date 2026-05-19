@echo off
setlocal
pushd "%~dp0.."
dotnet build .\FoToolbox.sln -c Release
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
