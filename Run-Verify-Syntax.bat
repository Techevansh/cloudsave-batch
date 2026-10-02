@echo off
setlocal
chcp 65001 >nul
cd /d C:\cloudsave-batch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\agent\verify-syntax.ps1"
echo.
pause
