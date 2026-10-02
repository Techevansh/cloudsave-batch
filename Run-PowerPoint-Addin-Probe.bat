@echo off
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0agent\powerpoint-addin-probe.ps1"
echo.
pause
