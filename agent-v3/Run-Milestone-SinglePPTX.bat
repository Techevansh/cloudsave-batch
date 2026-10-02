@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

REM ============================================================
REM  CloudSave Agent v3 - Single PPTX milestone test (Phase 1)
REM  Processes exactly ONE PowerPoint file, no subfolder recursion.
REM ============================================================
set CLOUDSAVE_MAX_FILES=1
set CLOUDSAVE_RECURSION=false
set CLOUDSAVE_RESUME=false

echo ============================================================
echo  CloudSave Agent v3 - Single PPTX milestone test
echo ============================================================
echo  1) Put ONE .pptx in a small test folder.
echo  2) Open that folder in Windows File Explorer and keep it visible.
echo  3) Emergency stop at any time: press F12.
echo ------------------------------------------------------------
echo  Starting automatically in 10 seconds...
echo  (no key needed - just make sure the Explorer folder is open)
timeout /t 10

echo.
echo  Running... watch Explorer / PowerPoint on screen.
cloudsave-agent.exe 1> milestone-log.jsonl 2>&1

echo.
echo ============================================================
echo  Finished. Full log saved next to this file:
echo    %cd%\milestone-log.jsonl
echo  Please send that file back so the run can be analyzed.
echo ============================================================
echo.
echo ----- log -----
type milestone-log.jsonl
echo.
echo  (this window stays open - close it when done)
timeout /t 86400 >nul
