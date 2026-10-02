@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

REM ============================================================
REM  CloudSave Agent v3 - Folder test (Phase 2-5)
REM  Processes ALL .pptx/.xlsx/.xls in the open folder AND its
REM  subfolders. Completed files are remembered and skipped on
REM  a re-run (resume). Use a SMALL test folder first, not the
REM  real work folder.
REM ============================================================
set CLOUDSAVE_MAX_FILES=0
set CLOUDSAVE_RECURSION=true
set CLOUDSAVE_RESUME=true

echo ============================================================
echo  CloudSave Agent v3 - Folder test (recursion ON)
echo ============================================================
echo  1) Open a SMALL test folder in Windows File Explorer
echo     (a few Office files, maybe one subfolder). Keep it visible.
echo  2) Emergency stop at any time: press F12.
echo  3) If a sign-in window appears, complete it; the agent waits.
echo ------------------------------------------------------------
echo  Starting automatically in 10 seconds...
timeout /t 10

echo.
echo  Running... watch Explorer / Office on screen.
cloudsave-agent.exe 1> folder-test-log.jsonl 2>&1

echo.
echo ============================================================
echo  Finished. Full log saved next to this file:
echo    %cd%\folder-test-log.jsonl
echo  Please send that file back.
echo ============================================================
echo.
echo ----- log -----
type folder-test-log.jsonl
echo.
echo  (this window stays open - close it when done)
timeout /t 86400 >nul
