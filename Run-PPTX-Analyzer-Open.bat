@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0agent\powerpoint-pptx-analyzer-open.ps1"
echo.
pause
