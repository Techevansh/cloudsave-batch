$ErrorActionPreference='Stop'
# CloudSave Batch - static PowerShell parser check.
# Runs the real Windows PowerShell / PowerShell language parser against every
# script under agent\ and reports syntax (parser) errors BEFORE the user runs
# anything. This is the gate required by the handoff: only parser-clean scripts
# are handed to the user.
#
# Usage (Windows PowerShell 5.1):
#   cd C:\cloudsave-batch
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\agent\verify-syntax.ps1"

$targetDir = Join-Path $PSScriptRoot ''
$scripts = Get-ChildItem -LiteralPath $PSScriptRoot -Filter *.ps1 -File | Sort-Object Name
if(-not $scripts){ Write-Host 'No .ps1 files found.'; exit 0 }

$failed = 0
foreach($s in $scripts){
 $tokens = $null
 $errors = $null
 [void][System.Management.Automation.Language.Parser]::ParseFile($s.FullName,[ref]$tokens,[ref]$errors)
 if($errors -and $errors.Count -gt 0){
  $failed++
  Write-Host ('FAIL  '+$s.Name+'  ('+$errors.Count+' parser error(s))')
  foreach($e in $errors){
   $ln = $e.Extent.StartLineNumber
   $col = $e.Extent.StartColumnNumber
   Write-Host ('   line '+$ln+' col '+$col+': '+$e.Message)
  }
 }else{
  Write-Host ('OK    '+$s.Name)
 }
}

Write-Host ''
if($failed -gt 0){
 Write-Host ('RESULT: '+$failed+' script(s) have parser errors. Do NOT run them until fixed.')
 exit 1
}else{
 Write-Host 'RESULT: all scripts passed the parser check.'
 exit 0
}
