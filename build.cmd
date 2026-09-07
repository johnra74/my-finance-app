@echo off
REM Windows shim for build.ps1.
REM
REM Windows 11 ships Windows PowerShell with script execution disabled by default, so
REM running .\build.ps1 directly fails with "running scripts is disabled on this system".
REM This wrapper bypasses the policy for this one script only -- it changes nothing
REM machine-wide -- and prefers PowerShell 7 when it is installed.
REM
REM Usage:  build [build^|test^|all^|run^|publish^|clean^|ef] [extra args...]

setlocal

where /q pwsh.exe
if %ERRORLEVEL% EQU 0 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
)

exit /b %ERRORLEVEL%
