@echo off
setlocal
chcp 65001 >nul
cd /d C:\cloudsave-batch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\agent\explorer-walker.ps1
echo.
pause
