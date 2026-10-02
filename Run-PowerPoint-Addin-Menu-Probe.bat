@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0agent\powerpoint-addin-menu-probe.ps1"
echo.
pause
