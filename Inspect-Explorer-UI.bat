@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\cloudsave-batch\agent\inspect-explorer.ps1"
echo.
pause
