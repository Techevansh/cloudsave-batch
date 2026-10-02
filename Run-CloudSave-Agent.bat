@echo off
setlocal
chcp 65001 >nul
cd /d C:\cloudsave-batch
for /f "usebackq delims=" %%A in (`powershell.exe -NoProfile -Command "$p='VTW 서버 (U:)'; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($p))"`) do set "DRIVE64=%%A"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\cloudsave-batch\agent\open-target.ps1" -DriveLabelBase64 "%DRIVE64%"
echo.
pause
