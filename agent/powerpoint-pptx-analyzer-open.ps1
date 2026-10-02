$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ExactClick {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public static void Click(int x,int y){
   SetCursorPos(x,y);
   mouse_event(0x0002,0,0,0,UIntPtr.Zero);
   mouse_event(0x0004,0,0,0,UIntPtr.Zero);
 }
}
'@

function Get-PowerPoint {
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
 $ppt=@()
 for($i=0;$i -lt $wins.Count;$i++){
  $w=$wins.Item($i)
  try{if($w.Current.ClassName -eq 'PPTFrameClass'){$ppt += $w}}catch{}
 }
 if(!$ppt){return $null}
 $target=$ppt | Where-Object {$_.Current.Name -match 'Summary'} | Select-Object -First 1
 if(!$target){$target=$ppt | Select-Object -First 1}
 return $target
}

function Find-Name($root,[string]$pattern){
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n)){continue}
   if($n -match $pattern){$hits += $e}
  }catch{}
 }
 return $hits
}

function Activate-Element($el){
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.InvokePattern]$p).Invoke()
  return 'InvokePattern'
 }
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.SelectionItemPattern]$p).Select()
  return 'SelectionItemPattern'
 }
 $r=$el.Current.BoundingRectangle
 if($r.Width -gt 0 -and $r.Height -gt 0 -and -not $el.Current.IsOffscreen){
  $x=[int]($r.X+$r.Width/2);$y=[int]($r.Y+$r.Height/2)
  [ExactClick]::Click($x,$y)
  return ('Exact UIA rectangle click @ '+$x+','+$y)
 }
 return $null
}

Write-Host ''
Write-Host 'CloudSave PPTX Analyzer Open Test v0.9'
Write-Host 'GUARDED ACTION: opens only the visible PPTX analyzer add-in.'
Write-Host 'No guessed coordinates. No keyboard input. Structure Analysis will NOT be clicked.'
Write-Host ''

$ppt=Get-PowerPoint
if(!$ppt){throw 'No PowerPoint window found.'}
Write-Host ('PowerPoint: '+$ppt.Current.Name)

$hits=@(Find-Name $ppt 'PPTX\s*analyzer|PPTX\s*분석|PPTX')
Write-Host ('PPTX-named UI candidates: '+$hits.Count)
$i=0
foreach($e in $hits){
 $i++
 $r=$e.Current.BoundingRectangle
 Write-Host ('['+$i+'] '+$e.Current.ControlType.ProgrammaticName+' | "'+$e.Current.Name+'" | id="'+$e.Current.AutomationId+'" | rect='+[int]$r.X+','+[int]$r.Y+','+[int]$r.Width+','+[int]$r.Height)
}
$target=$hits | Where-Object {$_.Current.Name -match '^PPTX\s*analyzer$'} | Select-Object -First 1
if(!$target){$target=$hits | Where-Object {$_.Current.Name -match 'PPTX.*analy'} | Select-Object -First 1}
if(!$target){throw 'Visible PPTX analyzer control was not found. Nothing was clicked.'}

$method=Activate-Element $target
if(!$method){throw 'PPTX analyzer was found but cannot be safely activated.'}
Write-Host ('Activated PPTX analyzer using: '+$method)
Start-Sleep -Seconds 2

# Re-read PowerPoint after task pane opens.
$panelHits=@(Find-Name $ppt 'PPTX 구조 분석기|구조 분석 시작|슬라이드 구성과 레이아웃')
Write-Host ('Task-pane verification candidates: '+$panelHits.Count)
$i=0
foreach($e in $panelHits){$i++;Write-Host ('['+$i+'] '+$e.Current.ControlType.ProgrammaticName+' | "'+$e.Current.Name+'" | id="'+$e.Current.AutomationId+'"')}

if($panelHits.Count -gt 0){
 Write-Host 'SUCCESS: PPTX analyzer task pane appears to be open.'
 Write-Host 'SAFE STOP: the Structure Analysis button was NOT clicked.'
}else{
 Write-Host 'WARNING: analyzer activation was sent, but task pane text was not exposed to UI Automation.'
 Write-Host 'SAFE STOP: no second action was attempted.'
}
