@echo off
setlocal
chcp 65001 >nul
cd /d C:\cloudsave-batch

for /f "usebackq delims=" %%A in (`powershell.exe -NoProfile -Command "$p='U:\부서 폴더\501. 공공부문 클라우드 네이티브 전문기술지원\10. 기술지원 2팀\099. 2팀_[개인폴더]\1. 백승훈 책임(H)'; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($p))"`) do set "TARGET64=%%A"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\cloudsave-batch\agent\open-target.ps1" -TargetBase64 "%TARGET64%"
echo.
pause
