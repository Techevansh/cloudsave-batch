param(
  [string]$TargetPath = "U:\부서 폴더\501. 공공부문 클라우드 네이티브 전문기술지원\10. 기술지원 2팀\099. 2팀_[개인폴더]\1. 백승훈 책임(H)"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "CloudSave UI Agent v0.5"
Write-Host "Opening target through Windows Explorer UI..."
Write-Host "Target: $TargetPath"
Write-Host ""

$wshell = New-Object -ComObject WScript.Shell
Start-Process explorer.exe

Start-Sleep -Seconds 2

if (-not $wshell.AppActivate("File Explorer")) {
  $null = $wshell.AppActivate("파일 탐색기")
}

Start-Sleep -Milliseconds 500

# Ctrl+L focuses Explorer's address bar. We intentionally use UI input
# instead of direct filesystem APIs because Cloudium blocks those APIs.
$wshell.SendKeys("^l")
Start-Sleep -Milliseconds 250

# Put the Unicode path on the clipboard and paste it into Explorer.
Set-Clipboard -Value $TargetPath
$wshell.SendKeys("^v")
Start-Sleep -Milliseconds 250
$wshell.SendKeys("{ENTER}")

Write-Host "Explorer navigation command sent."
Write-Host "Check whether Explorer opened the target folder."
Write-Host ""
