param(
  [string]$TargetPath = 'U:\부서 폴더\501. 공공부문 클라우드 네이티브 전문기술지원\10. 기술지원 2팀\099. 2팀_[개인폴더]\1. 백승훈 책임(H)'
)

$ErrorActionPreference = 'Stop'
$wshell = New-Object -ComObject WScript.Shell

Write-Host ''
Write-Host 'CloudSave UI Agent v0.5.1'
Write-Host ('Target: ' + $TargetPath)
Write-Host ''

# Launch Explorer without asking PowerShell/.NET to enumerate the protected drive.
Start-Process explorer.exe
Start-Sleep -Seconds 2

# Activate Explorer. Korean Windows may expose either title.
$activated = $wshell.AppActivate('File Explorer')
if (-not $activated) {
  $activated = $wshell.AppActivate('파일 탐색기')
}
if (-not $activated) {
  Write-Host '[ERROR] Explorer window could not be activated.'
  exit 2
}

Start-Sleep -Milliseconds 500

# Use Explorer's own UI only: focus address bar, paste path, press Enter.
$wshell.SendKeys('^l')
Start-Sleep -Milliseconds 300
Set-Clipboard -Value $TargetPath
$wshell.SendKeys('^v')
Start-Sleep -Milliseconds 300
$wshell.SendKeys('{ENTER}')

Write-Host '[OK] Navigation command was sent to Explorer.'
Write-Host 'Check whether Explorer is displaying the target folder.'
