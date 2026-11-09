@echo off
call "%~dp0Install.cmd" -Uninstall %*
exit /b %errorlevel%
