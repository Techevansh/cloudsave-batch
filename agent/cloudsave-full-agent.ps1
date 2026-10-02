$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class CSNative {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
 public static IntPtr[] Windows(){
  var a=new List<IntPtr>();
  EnumWindows(delegate(IntPtr h,IntPtr l){if(IsWindowVisible(h))a.Add(h);return true;},IntPtr.Zero);
  return a.ToArray();
 }
 public static string Title(IntPtr h){var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
 public static string Class(IntPtr h){var s=new StringBuilder(256);GetClassName(h,s,s.Capacity);return s.ToString();}
 public static void Click(int x,int y){SetCursorPos(x,y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
 public static void DoubleClick(int x,int y){Click(x,y);System.Threading.Thread.Sleep(130);Click(x,y);}
 public static bool StopPressed(){return (GetAsyncKeyState(0x7B) & 0x8000) != 0;} // F12
}
'@

function U([int[]]$c){ return -join ($c|ForEach-Object{[char]$_}) }
$KFolder=U @(0xD30C,0xC77C,0x20,0xD3F4,0xB354)
$KBack=U @(0xB4A4,0xB85C)
$KStart=U @(0xAD6C,0xC870,0x20,0xBD84,0xC11D,0x20,0xC2DC,0xC791)
$KBusy=U @(0xBD84,0xC11D,0x20,0xC911,0x2E,0x2E,0x2E)

$Script:Config=[pscustomobject]@{
 MaxDepth=10
 OfficeOpenTimeoutSec=90
 AnalyzerOpenTimeoutSec=20
 AnalysisTimeoutSec=600
 CloseTimeoutSec=20
 DryRun=$false
}
$Script:LogDir=Join-Path $PSScriptRoot '..\logs'
New-Item -ItemType Directory -Force -Path $Script:LogDir|Out-Null
$Script:LogFile=Join-Path $Script:LogDir ('run-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$Script:Processed=New-Object System.Collections.Generic.HashSet[string]

function Log([string]$m){
 $line=('['+(Get-Date -Format 'HH:mm:ss')+'] '+$m)
 Write-Host $line
 Add-Content -LiteralPath $Script:LogFile -Value $line -Encoding UTF8
}
function Stop-IfRequested {
 if([CSNative]::StopPressed()){throw 'EMERGENCY_STOP_F12'}
}
function RootFromHandle([IntPtr]$h){ return [System.Windows.Automation.AutomationElement]::FromHandle($h) }
function Get-ExplorerWindows {
 $a=@()
 foreach($h in [CSNative]::Windows()){
  if([CSNative]::Class($h) -eq 'CabinetWClass'){$a+=$h}
 }
 return $a
}
function Get-DescendantText($e){
 $parts=@()
 try{
  $d=$e.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  for($i=0;$i -lt $d.Count;$i++){try{$n=$d.Item($i).Current.Name;if($n){$parts+=$n}}catch{}}
 }catch{}
 return ($parts -join ' | ')
}
function Classify-Row($e,$name){
 $ext=[IO.Path]::GetExtension($name).ToLowerInvariant()
 if($ext -in @('.pptx','.xlsx','.xls')){return 'OFFICE'}
 $meta=Get-DescendantText $e
 try{$help=$e.Current.HelpText}catch{$help=''}
 if(($meta -and $meta.Contains($KFolder)) -or ($help -and $help.Contains($KFolder))){return 'FOLDER'}
 if([string]::IsNullOrWhiteSpace($ext)){return 'FOLDER'}
 return 'SKIP'
}
function Read-ExplorerRows([IntPtr]$h){
 $root=RootFromHandle $h
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@();$seen=@{}
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $type=$e.Current.ControlType.ProgrammaticName;$name=$e.Current.Name
   if($type -notin @('ControlType.DataItem','ControlType.ListItem') -or [string]::IsNullOrWhiteSpace($name)){continue}
   $r=$e.Current.BoundingRectangle
   if($r.Width -le 0 -or $r.Height -le 0){continue}
   $key=$type+'|'+$name
   if($seen[$key]){continue};$seen[$key]=$true
   $rows += [pscustomobject]@{Name=$name;Kind=(Classify-Row $e $name);Element=$e;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
  }catch{}
 }
 return $rows
}
function Select-StartExplorer {
 $c=@()
 foreach($h in Get-ExplorerWindows){
  try{
   $rows=@(Read-ExplorerRows $h)
   $office=@($rows|Where-Object Kind -eq 'OFFICE').Count
   $folders=@($rows|Where-Object Kind -eq 'FOLDER').Count
   $score=$rows.Count+($office*10)+($folders*2)
   $c += [pscustomobject]@{H=$h;Rows=$rows;Score=$score;Title=[CSNative]::Title($h)}
  }catch{}
 }
 if(!$c){throw 'No readable File Explorer window found. Open the desired start folder first.'}
 $x=$c|Sort-Object Score -Descending|Select-Object -First 1
 Log ('START_EXPLORER "'+$x.Title+'" items='+$x.Rows.Count+' office='+@($x.Rows|Where-Object Kind -eq 'OFFICE').Count+' folders='+@($x.Rows|Where-Object Kind -eq 'FOLDER').Count)
 return $x.H
}
function Activate-Element($item,[bool]$double=$false){
 Stop-IfRequested
 $e=$item.Element;$p=$null
 if(!$double -and $e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.InvokePattern]$p).Invoke();return 'InvokePattern'
 }
 $x=[int]($item.X+$item.W/2);$y=[int]($item.Y+$item.H/2)
 if($double){[CSNative]::DoubleClick($x,$y)}else{[CSNative]::Click($x,$y)}
 return ('UIA_RECT@'+$x+','+$y)
}
function WindowMap {
 $m=@{}
 foreach($h in [CSNative]::Windows()){$m[[string]$h.ToInt64()]=[CSNative]::Title($h)}
 return $m
}
function Find-OfficeWindow([string]$ext,[string]$base,[int]$timeout){
 $deadline=(Get-Date).AddSeconds($timeout)
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  foreach($h in [CSNative]::Windows()){
   $cls=[CSNative]::Class($h);$title=[CSNative]::Title($h)
   if($ext -eq '.pptx' -and $cls -eq 'PPTFrameClass' -and $title -like ('*'+$base+'*')){return $h}
   if($ext -in @('.xlsx','.xls') -and $cls -eq 'XLMAIN' -and $title -like ('*'+$base+'*')){return $h}
  }
  Start-Sleep -Milliseconds 300
 }
 return [IntPtr]::Zero
}
function Find-VisibleByName([IntPtr]$h,[string]$name,[bool]$contains=$false){
 $root=RootFromHandle $h
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n) -or $e.Current.IsOffscreen){continue}
   $ok=if($contains){$n.Contains($name)}else{$n -eq $name}
   if($ok){
    $r=$e.Current.BoundingRectangle
    if($r.Width -gt 0 -and $r.Height -gt 0){$hits += [pscustomobject]@{Name=$n;Element=$e;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}}
   }
  }catch{}
 }
 return $hits
}
function Ensure-AnalyzerPane([IntPtr]$office,[string]$ext){
 if(@(Find-VisibleByName $office $KStart $false).Count -gt 0){return}
 $label=if($ext -eq '.pptx'){'PPTX analyzer'}else{'Excel analyzer'}
 $hits=@(Find-VisibleByName $office $label $false)
 if($hits.Count -ne 1){throw ('Analyzer ribbon button not uniquely found: '+$label+' count='+$hits.Count)}
 $null=Activate-Element $hits[0] $false
 $deadline=(Get-Date).AddSeconds($Script:Config.AnalyzerOpenTimeoutSec)
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  if(@(Find-VisibleByName $office $KStart $false).Count -gt 0){return}
  Start-Sleep -Milliseconds 250
 }
 throw 'Analyzer task pane did not expose the start button in time.'
}
function Run-Analysis([IntPtr]$office,[string]$logical){
 Ensure-AnalyzerPane $office ([IO.Path]::GetExtension($logical).ToLowerInvariant())
 $start=@(Find-VisibleByName $office $KStart $false)
 if($start.Count -ne 1){throw ('Analysis start button count='+$start.Count)}
 $baseline=WindowMap
 $null=Activate-Element $start[0] $false
 Log ('ANALYSIS_START '+$logical)
 $deadline=(Get-Date).AddSeconds($Script:Config.AnalysisTimeoutSec)
 $seenWorking=$false;$reportedAuth=@{}
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  $ready=@(Find-VisibleByName $office $KStart $false).Count -gt 0
  $busy=@(Find-VisibleByName $office $KBusy $false).Count -gt 0
  if(!$ready -or $busy){$seenWorking=$true}
  $now=WindowMap
  foreach($k in $now.Keys){
   if(!$baseline.ContainsKey($k) -and !$reportedAuth[$k]){
    $t=$now[$k]
    if($t -and $t -notmatch 'PowerShell'){
     Log ('INTERACTIVE_WINDOW "'+$t+'" - complete sign-in/consent manually if requested')
     $reportedAuth[$k]=$true
    }
   }
  }
  if($seenWorking -and $ready){
   Log ('ANALYSIS_FINISHED '+$logical)
   return
  }
  Start-Sleep -Milliseconds 250
 }
 throw 'Analysis timeout. A sign-in/consent window may still be waiting.'
}
function Close-Office([IntPtr]$h){
 try{
  $root=RootFromHandle $h;$p=$null
  if($root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$p)){
   ([System.Windows.Automation.WindowPattern]$p).Close()
  }else{throw 'No WindowPattern'}
 }catch{Log ('WARN close failed: '+$_.Exception.Message);return}
 $deadline=(Get-Date).AddSeconds($Script:Config.CloseTimeoutSec)
 while((Get-Date)-lt $deadline){
  $exists=$false
  foreach($x in [CSNative]::Windows()){if($x -eq $h){$exists=$true;break}}
  if(!$exists){return}
  Start-Sleep -Milliseconds 300
 }
 Log 'WARN Office window still open after close timeout; continuing without force-kill.'
}
function Process-OfficeItem([IntPtr]$explorer,$item,[string]$logical){
 if($Script:Processed.Contains($logical)){return}
 $ext=[IO.Path]::GetExtension($item.Name).ToLowerInvariant()
 $base=[IO.Path]::GetFileNameWithoutExtension($item.Name)
 Log ('OPEN '+$logical)
 if($Script:Config.DryRun){$Script:Processed.Add($logical)|Out-Null;return}
 $null=Activate-Element $item $true
 $office=Find-OfficeWindow $ext $base $Script:Config.OfficeOpenTimeoutSec
 if($office -eq [IntPtr]::Zero){throw ('Office window timeout for '+$item.Name)}
 try{
  Run-Analysis $office $logical
  $Script:Processed.Add($logical)|Out-Null
 }finally{
  Close-Office $office
 }
}
function Click-Back([IntPtr]$explorer){
 $root=RootFromHandle $explorer
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if(($n -eq $KBack -or $n -eq 'Back') -and !$e.Current.IsOffscreen){
    $r=$e.Current.BoundingRectangle
    if($r.Width -gt 0 -and $r.Height -gt 0){$hits += [pscustomobject]@{Element=$e;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}}
   }
  }catch{}
 }
 if(!$hits){throw 'Explorer Back button not found.'}
 $null=Activate-Element $hits[0] $false
 Start-Sleep -Milliseconds 700
}
function Enter-Folder([IntPtr]$explorer,$item){
 $before=[CSNative]::Title($explorer)
 $null=Activate-Element $item $true
 $deadline=(Get-Date).AddSeconds(10)
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  $now=[CSNative]::Title($explorer)
  if($now -ne $before){return}
  Start-Sleep -Milliseconds 250
 }
 # Some Explorer tabs keep a generic title; accept changed row signature as fallback.
 Start-Sleep -Milliseconds 500
}
function Walk-Folder([IntPtr]$explorer,[string]$logical,[int]$depth){
 Stop-IfRequested
 if($depth -gt $Script:Config.MaxDepth){Log ('SKIP_DEPTH '+$logical);return}
 $rows=@(Read-ExplorerRows $explorer)
 Log ('SCAN '+$logical+' items='+$rows.Count)
 # Process files first so a folder navigation does not invalidate cached UI elements.
 foreach($r in @($rows|Where-Object Kind -eq 'OFFICE')){
  Stop-IfRequested
  $key=if($logical){$logical+'\'+$r.Name}else{$r.Name}
  try{Process-OfficeItem $explorer $r $key}catch{Log ('ERROR '+$key+' :: '+$_.Exception.Message)}
 }
 # Refresh before folder traversal.
 $folderNames=@($rows|Where-Object Kind -eq 'FOLDER'|ForEach-Object {$_.Name})
 foreach($fname in $folderNames){
  Stop-IfRequested
  $fresh=@(Read-ExplorerRows $explorer|Where-Object {$_.Kind -eq 'FOLDER' -and $_.Name -eq $fname}|Select-Object -First 1)
  if(!$fresh){Log ('WARN folder disappeared: '+$fname);continue}
  $child=if($logical){$logical+'\'+$fname}else{$fname}
  try{
   Log ('ENTER '+$child)
   Enter-Folder $explorer $fresh[0]
   Walk-Folder $explorer $child ($depth+1)
   Click-Back $explorer
   Log ('BACK '+$logical)
  }catch{
   Log ('ERROR_FOLDER '+$child+' :: '+$_.Exception.Message)
   try{Click-Back $explorer}catch{}
  }
 }
}

Write-Host ''
Write-Host 'CloudSave Full UI Agent v2.0'
Write-Host 'End-to-end state machine: Explorer -> Office -> Analyzer -> Login-if-needed -> Analysis -> Close -> recurse.'
Write-Host 'Emergency stop: press F12 at any time.'
Write-Host 'IMPORTANT: open the desired START folder in File Explorer before running.'
Write-Host ''
$explorer=Select-StartExplorer
Log ('RUN_START log='+$Script:LogFile)
try{
 Walk-Folder $explorer '' 0
 Log ('RUN_COMPLETE processed='+$Script:Processed.Count)
}catch{
 Log ('RUN_ABORT '+$_.Exception.Message)
}
Write-Host ''
Write-Host ('Finished. Processed Office files: '+$Script:Processed.Count)
Write-Host ('Log: '+$Script:LogFile)
