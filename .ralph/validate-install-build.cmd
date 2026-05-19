@echo off
setlocal
pushd "%~dp0.."
dotnet build .\install\
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
