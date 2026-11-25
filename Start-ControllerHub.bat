@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "mode="
if "%~1"=="" set "mode=--wpf"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-preview.ps1" %mode% %*
set "result=%errorlevel%"
popd
if not "%result%"=="0" pause
exit /b %result%
