$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SafeUiClick {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public static void Click(int x,int y){
  SetCursorPos(x,y);
  mouse_event(0x0002,0,0,0,UIntPtr.Zero);
  mouse_event(0x0004,0,0,0,UIntPtr.Zero);
 }
}
'@

function Find-VisibleByName([string]$pattern){
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n) -or $e.Current.IsOffscreen){continue}
   if($n -match $pattern){
    $r=$e.Current.BoundingRectangle
    if($r.Width -gt 0 -and $r.Height -gt 0){
      $hits += [pscustomobject]@{Element=$e;Name=$n;Type=$e.Current.ControlType.ProgrammaticName;Id=$e.Current.AutomationId;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
    }
   }
  }catch{}
 }
 return $hits
}

function Activate($hit){
 $e=$hit.Element
 $p=$null
 if($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.InvokePattern]$p).Invoke(); return 'InvokePattern'
 }
 $x=[int]($hit.X+$hit.W/2);$y=[int]($hit.Y+$hit.H/2)
 [SafeUiClick]::Click($x,$y)
 return ('Exact UIA rectangle click @ '+$x+','+$y)
}

Write-Host ''
Write-Host 'CloudSave Single PPTX Structure Analysis v1.0'
Write-Host 'GUARDED: exactly one visible "구조 분석 시작" button will be activated.'
Write-Host 'No Explorer traversal. No second Office file. No loop.'
Write-Host ''

$buttons=@(Find-VisibleByName '구조 분석 시작')
Write-Host ('Visible Structure Analysis candidates: '+$buttons.Count)
$i=0
foreach($b in $buttons){$i++;Write-Host ('['+$i+'] '+$b.Type+' | "'+$b.Name+'" | id="'+$b.Id+'" | rect='+$b.X+','+$b.Y+','+$b.W+','+$b.H)}
if($buttons.Count -ne 1){
 throw ('Expected exactly one visible Structure Analysis button, found '+$buttons.Count+'. Nothing was clicked.')
}
$method=Activate $buttons[0]
Write-Host ('Activated using: '+$method)
Write-Host 'Waiting for analyzer status for up to 30 seconds...'

$deadline=(Get-Date).AddSeconds(30)
$last=''
while((Get-Date)-lt $deadline){
 Start-Sleep -Seconds 1
 $status=@(Find-VisibleByName '분석|검사|파싱|완성|중단|오류|준비')
 $texts=@($status|ForEach-Object {$_.Name}|Select-Object -Unique)
 $joined=$texts -join ' | '
 if($joined -and $joined -ne $last){
   Write-Host ('STATUS: '+$joined)
   $last=$joined
 }
 if($joined -match '분석 리포트 완성|분석 준비됨|검사 중단'){break}
}
Write-Host ''
Write-Host 'SAFE STOP: one Structure Analysis activation was attempted. No next file will be opened.'
