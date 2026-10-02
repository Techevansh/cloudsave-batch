param(
  [string]$TargetBase64
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($TargetBase64)) {
  Write-Host 'ERROR: target path was not supplied.'
  exit 2
}

$TargetPath = [System.Text.Encoding]::UTF8.GetString(
  [System.Convert]::FromBase64String($TargetBase64)
)

Write-Host ''
Write-Host 'CloudSave UI Agent v0.5.2'
Write-Host ('Target: ' + $TargetPath)
Write-Host ''

$wshell = New-Object -ComObject WScript.Shell

Start-Process explorer.exe
Start-Sleep -Seconds 2

$explorer = Get-Process explorer -ErrorAction SilentlyContinue |
  Where-Object { $_.MainWindowHandle -ne 0 } |
  Select-Object -First 1

if ($null -eq $explorer) {
  Write-Host 'ERROR: Explorer window was not found.'
  exit 3
}

$activated = $wshell.AppActivate($explorer.Id)
if (-not $activated) {
  Write-Host 'ERROR: Explorer window could not be activated.'
  exit 4
}

Start-Sleep -Milliseconds 500
$wshell.SendKeys('^l')
Start-Sleep -Milliseconds 300
Set-Clipboard -Value $TargetPath
$wshell.SendKeys('^v')
Start-Sleep -Milliseconds 300
$wshell.SendKeys('{ENTER}')

Write-Host 'OK: navigation command sent to Explorer.'
Write-Host 'Check the Explorer window.'
