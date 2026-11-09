@echo off
setlocal
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
set "INSTALL_RESULT=%errorlevel%"
echo.
pause
exit /b %INSTALL_RESULT%
