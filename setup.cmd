@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
set "SETUP_EXIT_CODE=%ERRORLEVEL%"
echo.
if "%SETUP_EXIT_CODE%"=="0" (
    echo Setup finished. Press any key to close this window, or close the window now.
) else (
    echo Setup stopped with an error. Press any key to close this window, or close the window now.
)
pause >nul
exit /b %SETUP_EXIT_CODE%
