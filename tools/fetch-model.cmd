@echo off
REM Windows shim for fetch-model.ps1.
REM
REM Windows 11 ships Windows PowerShell with script execution disabled by default, so
REM running .\tools\fetch-model.ps1 directly fails with "running scripts is disabled on
REM this system". This wrapper bypasses the policy for this one script only -- it changes
REM nothing machine-wide -- and prefers PowerShell 7 when it is installed.
REM
REM Usage:  tools\fetch-model

setlocal

where /q pwsh.exe
if %ERRORLEVEL% EQU 0 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-model.ps1" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-model.ps1" %*
)

exit /b %ERRORLEVEL%
