@echo off
setlocal
chcp 65001 >nul
cd /d C:\cloudsave-batch

powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\agent\validate-full-agent.ps1"
if errorlevel 1 (
  echo.
  echo [STOP] Validation failed. The agent was NOT started.
  echo.
  pause
  exit /b 1
)

echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\agent\cloudsave-full-agent.ps1"
echo.
pause
