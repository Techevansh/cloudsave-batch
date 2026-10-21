@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title CloudSave Agent - single file test

REM ============================================================
REM  CloudSave Agent v3 - single file test
REM  Processes only the FIRST Office file of the open folder,
REM  no subfolders, and remembers nothing (so a test never makes
REM  a later real run skip a file). Emergency stop: F12.
REM  A full JSONL log is written to .\logs\
REM ============================================================
set CLOUDSAVE_CONSOLE=human
set CLOUDSAVE_RECURSION=false
set CLOUDSAVE_MAX_FILES=1
set CLOUDSAVE_RESUME=false
set CLOUDSAVE_START_DELAY_SEC=10

cloudsave-agent.exe

echo.
echo  (This window can be closed now.)
timeout /t 86400 >nul
