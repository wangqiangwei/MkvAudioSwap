@echo off
rem Launcher only. Build logic lives in build.ps1 (UTF-8 with BOM).
rem Pure ASCII on purpose: cmd.exe reads .bat using the ANSI codepage,
rem and some Chinese characters in GBK end with byte 0x5C (backslash),
rem which cmd treats as a line continuation and corrupts the script.
setlocal
cd /d "%~dp0"

where powershell >nul 2>&1
if %errorlevel%==0 (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
    goto :done
)

where pwsh >nul 2>&1
if %errorlevel%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
    goto :done
)

echo [X] PowerShell not found. Please run build.ps1 manually.
pause

:done
endlocal