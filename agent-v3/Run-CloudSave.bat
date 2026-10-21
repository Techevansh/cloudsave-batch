@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title CloudSave Agent

REM ============================================================
REM  CloudSave Agent v3 - cloud-save a whole folder
REM
REM  1) Open the TOP folder to process in File Explorer.
REM  2) Run this file.
REM  Every .pptx / .xlsx / .xls in that folder and all of its
REM  subfolders is opened one by one, cloud-saved by the add-in,
REM  and closed WITHOUT saving. Emergency stop: F12.
REM
REM  The agent prints the (Korean) instructions, the chosen folder,
REM  a 10 second countdown and progress itself. This file is kept
REM  ASCII-only on purpose so cmd.exe always parses it correctly.
REM  A full JSONL log of every run is written to .\logs\
REM ============================================================
set CLOUDSAVE_CONSOLE=human
set CLOUDSAVE_RECURSION=true
set CLOUDSAVE_MAX_FILES=0
set CLOUDSAVE_RESUME=true
set CLOUDSAVE_START_DELAY_SEC=10

cloudsave-agent.exe

echo.
echo  (This window can be closed now.)
timeout /t 86400 >nul
