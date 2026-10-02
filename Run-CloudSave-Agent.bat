@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\cloudsave-batch\agent\open-target.ps1"
pause
