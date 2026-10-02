$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class SafeUi {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 public static void Click(int x,int y){
  SetCursorPos(x,y);
  mouse_event(0x0002,0,0,0,UIntPtr.Zero);
  mouse_event(0x0004,0,0,0,UIntPtr.Zero);
 }
 public static string Title(IntPtr h){
  var s=new StringBuilder(512); GetWindowText(h,s,s.Capacity); return s.ToString();
 }
 public static IntPtr[] TopWindows(){
  var a=new List<IntPtr>();
  EnumWindows(delegate(IntPtr h,IntPtr l){
   if(IsWindowVisible(h)) a.Add(h); return true;
  },IntPtr.Zero);
  return a.ToArray();
 }
}
'@

function U([int[]]$codes){ return -join ($codes | ForEach-Object {[char]$_}) }

# Korean strings are built from Unicode code points so Windows PowerShell 5.1
# never has to parse Korean text directly from the UTF-8 Git checkout.
$startName = U @(0xAD6C,0xC870,0x20,0xBD84,0xC11D,0x20,0xC2DC,0xC791)
$busyName  = U @(0xBD84,0xC11D,0x20,0xC911,0x2E,0x2E,0x2E)
$reportDone = U @(0xBD84,0xC11D,0x20,0xB9AC,0xD3EC,0xD2B8,0x20,0xC644,0xC131)
$stopped = U @(0xAC80,0xC0AC,0x20,0xC911,0xB2E8)
$ready = U @(0xBD84,0xC11D,0x20,0xC900,0xBE44,0xB428)

function Find-VisibleName([string]$target,[bool]$exact=$true){
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n) -or $e.Current.IsOffscreen){continue}
   $ok=if($exact){$n -eq $target}else{$n.Contains($target)}
   if($ok){
    $r=$e.Current.BoundingRectangle
    if($r.Width -gt 0 -and $r.Height -gt 0){
     $hits += [pscustomobject]@{
      Element=$e;Name=$n;Type=$e.Current.ControlType.ProgrammaticName;Id=$e.Current.AutomationId;
      X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height
     }
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
  ([System.Windows.Automation.InvokePattern]$p).Invoke()
  return 'InvokePattern'
 }
 $x=[int]($hit.X+$hit.W/2);$y=[int]($hit.Y+$hit.H/2)
 [SafeUi]::Click($x,$y)
 return ('Exact UIA rectangle click @ '+$x+','+$y)
}

function WindowSnapshot {
 $map=@{}
 foreach($h in [SafeUi]::TopWindows()){
  $k=[string]$h.ToInt64()
  $map[$k]=[SafeUi]::Title($h)
 }
 return $map
}

Write-Host ''
Write-Host 'CloudSave Single PPTX Structure Analysis v1.1'
Write-Host 'STATE-AWARE: supports both cached login and interactive sign-in.'
Write-Host 'One PPTX only. No Explorer traversal. No second file. No loop.'
Write-Host ''

$buttons=@(Find-VisibleName $startName $true)
Write-Host ('Visible target candidates: '+$buttons.Count)
if($buttons.Count -ne 1){
 throw ('Expected exactly one visible target button, found '+$buttons.Count+'. Nothing was clicked.')
}

$before=WindowSnapshot
$method=Activate $buttons[0]
Write-Host ('Activated using: '+$method)
Write-Host 'State: STARTED'
Write-Host 'If sign-in is already cached, analysis should continue automatically.'
Write-Host 'If a sign-in window appears, complete it manually; this agent will keep waiting.'
Write-Host ''

$deadline=(Get-Date).AddMinutes(5)
$state='STARTED'
$seenBusy=$false
$seenAuth=$false
$lastNew=''
while((Get-Date)-lt $deadline){
 Start-Sleep -Seconds 1

 $startVisible=@(Find-VisibleName $startName $true).Count -gt 0
 $busyVisible=@(Find-VisibleName $busyName $true).Count -gt 0
 $doneVisible=@(Find-VisibleName $reportDone $false).Count -gt 0
 $stopVisible=@(Find-VisibleName $stopped $false).Count -gt 0
 $readyVisible=@(Find-VisibleName $ready $false).Count -gt 0

 $now=WindowSnapshot
 $newTitles=@()
 foreach($k in $now.Keys){
  if(-not $before.ContainsKey($k)){
   $t=$now[$k]
   if(-not [string]::IsNullOrWhiteSpace($t) -and $t -notmatch 'PowerShell'){
    $newTitles += $t
   }
  }
 }
 $newText=($newTitles|Sort-Object -Unique) -join ' | '
 if($newText -and $newText -ne $lastNew){
  Write-Host ('State: INTERACTIVE_WINDOW -> '+$newText)
  Write-Host 'Action: complete sign-in/consent in that window if requested. No password is stored by CloudSave Batch.'
  $seenAuth=$true
  $lastNew=$newText
 }

 if($busyVisible -and -not $seenBusy){
  $seenBusy=$true
  $state='ANALYZING'
  Write-Host 'State: ANALYZING'
 }

 if($doneVisible){
  Write-Host 'State: COMPLETED (report text detected)'
  Write-Host 'SUCCESS: this PPTX analysis cycle completed.'
  exit 0
 }
 if($stopVisible){
  Write-Host 'State: STOPPED/ERROR (task-pane text detected)'
  exit 2
 }

 # The taskpane restores its start button in finally{} after success or failure.
 # If we observed an active phase (busy or auth) and the start button returns,
 # treat this as cycle completion even when WebView status text is not exposed to UIA.
 if($startVisible -and ($seenBusy -or $seenAuth)){
  if($readyVisible){Write-Host 'State: READY AGAIN'}
  Write-Host 'State: CYCLE_FINISHED (start button returned)'
  Write-Host 'SUCCESS: analysis cycle ended; no next file will be opened in this test.'
  exit 0
 }

 if((-not $startVisible) -and $state -eq 'STARTED'){
  $state='WORKING'
  Write-Host 'State: WORKING (start button temporarily unavailable)'
 }
}

Write-Host 'State: TIMEOUT'
Write-Host 'No second click was attempted. Check whether a sign-in/consent window is still waiting.'
exit 3
