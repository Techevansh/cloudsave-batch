@echo off
setlocal
chcp 65001 >nul
set "TARGET=%~dp0"
set "CLOUDSAVE_HOME=C:\cloudsave-batch"

echo.
echo ========================================
echo   CloudSave Batch - Scan This Folder
echo ========================================
echo Target: %TARGET%
echo.

if not exist "%CLOUDSAVE_HOME%\src\index.js" (
  echo [ERROR] CloudSave engine was not found:
  echo %CLOUDSAVE_HOME%
  echo.
  pause
  exit /b 1
)

node "%CLOUDSAVE_HOME%\src\index.js" "%TARGET%"
echo.
pause
