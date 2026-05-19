@echo off
setlocal
pushd "%~dp0.."
dotnet test .\FoToolbox.sln -c Release --filter TestifyConfiguration
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
