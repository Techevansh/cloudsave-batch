$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# =============================================================================
# CloudSave Full UI Agent v3.0
# -----------------------------------------------------------------------------
# Clean, single-copy rewrite that reuses the pieces already proven on the real
# VTW (U:) Cloudium environment:
#   * Explorer reader             (from explorer-walker.ps1)
#   * Office file activation      (from single-office-open.ps1)
#   * Analyzer ribbon open        (from powerpoint-pptx-analyzer-open.ps1)
#   * Structure-analysis click +  (from single-pptx-structure-analysis.ps1)
#     cached/interactive auth state machine
#
# Design rules enforced here (see CLAUDE_CODE_HANDOFF.md sections 15, 16, 22):
#   * No U: filesystem access (no Get-ChildItem / fs.readdir on U:).
#   * No SendKeys, no Ctrl+A/F6, no guessed screen coordinates.
#   * Only InvokePattern / SelectionItemPattern, with the ELEMENT's real
#     BoundingRectangle centre as the single allowed click fallback.
#   * No Korean string literals in source. All Korean is assembled from Unicode
#     code points via U(), so Windows PowerShell 5.1 + UTF-8 Git checkout never
#     has to parse Korean text (this was a real past cause of parser breakage).
#   * Emergency stop: F12, polled inside every loop and before every action.
#   * Office is closed with WindowPattern.Close only, never a process kill.
#   * Depth-first traversal; UI elements are re-looked-up fresh after navigation.
#   * JSON resume DB so completed files are skipped on re-run.
#   * Recursion and a file cap are config-gated for phased testing.
# =============================================================================

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
 public static bool StopPressed(){return (GetAsyncKeyState(0x7B) & 0x8000) != 0;} // 0x7B = F12
}
'@

# ---- Korean strings built from Unicode code points (no source literals) -----
function U([int[]]$c){ return -join ($c|ForEach-Object{[char]$_}) }
$KStart      = U @(0xAD6C,0xC870,0x20,0xBD84,0xC11D,0x20,0xC2DC,0xC791)        # "gujo bunseok sijak"  (Start structure analysis - the blue task-pane button)
$KBusy       = U @(0xBD84,0xC11D,0x20,0xC911,0x2E,0x2E,0x2E)                   # "bunseok jung..."     (Analyzing...)
$KDone       = U @(0xBD84,0xC11D,0x20,0xB9AC,0xD3EC,0xD2B8,0x20,0xC644,0xC131)  # "bunseok ripoteu wanseong" (Analysis report complete)
$KStopped    = U @(0xAC80,0xC0AC,0x20,0xC911,0xB2E8)                           # "geomsa jungdan"      (Inspection stopped)
$KReady      = U @(0xBD84,0xC11D,0x20,0xC900,0xBE44,0xB428)                    # "bunseok junbidoem"   (Analysis ready)
$KFolder     = U @(0xD30C,0xC77C,0x20,0xD3F4,0xB354)                           # "pail polder"         (File folder - Explorer type column)
$KBack       = U @(0xB4A4,0xB85C)                                             # "dwiro"               (Back - Explorer navigation)
$KEnableEdit = U @(0xD3B8,0xC9D1,0x20,0xC0AC,0xC6A9)                           # "pyeonjip sayong"     (Enable Editing - Protected View banner)
$KHome       = U @(0xD648)                                                    # "hom"                 (Home - ribbon tab holding the analyzer group)

# ---- Config (file overrides defaults) ---------------------------------------
$Script:Config=[pscustomobject]@{
 MaxDepth=10
 OfficeOpenTimeoutSec=90
 AnalyzerOpenTimeoutSec=45
 AnalysisTimeoutSec=600
 CloseTimeoutSec=20
 SupportedExtensions=@('.pptx','.xlsx','.xls')
 PowerPointAnalyzerLabel='PPTX analyzer'
 ExcelAnalyzerLabel='Excel analyzer'
 EnableRecursion=$true
 MaxOfficeFiles=0      # 0 = unlimited. Set to 1 for Phase 1 single-file testing.
 StateResume=$true
 EnableProtectedViewEdit=$true
 DryRun=$false
}
$cfgPath=Join-Path $PSScriptRoot 'full-agent.config.json'
if(Test-Path -LiteralPath $cfgPath){
 try{
  $j=Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8|ConvertFrom-Json
  foreach($p in $j.PSObject.Properties){
   $name=$p.Name
   $map=@{
    maxDepth='MaxDepth';officeOpenTimeoutSec='OfficeOpenTimeoutSec';
    analyzerOpenTimeoutSec='AnalyzerOpenTimeoutSec';analysisTimeoutSec='AnalysisTimeoutSec';
    closeTimeoutSec='CloseTimeoutSec';supportedExtensions='SupportedExtensions';
    powerPointAnalyzerLabel='PowerPointAnalyzerLabel';excelAnalyzerLabel='ExcelAnalyzerLabel';
    enableRecursion='EnableRecursion';maxOfficeFiles='MaxOfficeFiles';
    stateResume='StateResume';enableProtectedViewEdit='EnableProtectedViewEdit';dryRun='DryRun'
   }
   if($map.ContainsKey($name)){ $Script:Config.($map[$name])=$p.Value }
  }
 }catch{ Write-Host ('WARN could not read config, using defaults: '+$_.Exception.Message) }
}

# ---- Logging + state DB -----------------------------------------------------
$Script:LogDir=Join-Path $PSScriptRoot '..\logs'
New-Item -ItemType Directory -Force -Path $Script:LogDir|Out-Null
$Script:LogFile=Join-Path $Script:LogDir ('run-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$Script:StateFile=Join-Path $Script:LogDir 'state.json'
$Script:State=@{}
$Script:OfficeCount=0

function Log([string]$m){
 $line=('['+(Get-Date -Format 'HH:mm:ss')+'] '+$m)
 Write-Host $line
 Add-Content -LiteralPath $Script:LogFile -Value $line -Encoding UTF8
}
function Load-State {
 if($Script:Config.StateResume -and (Test-Path -LiteralPath $Script:StateFile)){
  try{
   $raw=Get-Content -LiteralPath $Script:StateFile -Raw -Encoding UTF8
   if($raw){
    $obj=$raw|ConvertFrom-Json
    foreach($p in $obj.PSObject.Properties){ $Script:State[$p.Name]=$p.Value }
   }
  }catch{ Log ('WARN could not load state DB: '+$_.Exception.Message) }
 }
}
function Save-State {
 try{
  ($Script:State|ConvertTo-Json -Depth 5)|Set-Content -LiteralPath $Script:StateFile -Encoding UTF8
 }catch{ Log ('WARN could not save state DB: '+$_.Exception.Message) }
}
function Record-State([string]$key,[string]$status,[string]$ext,[string]$reason){
 $Script:State[$key]=[pscustomobject]@{
  logicalPath=$key
  extension=$ext
  status=$status
  failureReason=$reason
  updatedAt=(Get-Date -Format 's')
 }
 Save-State
}
function Is-Completed([string]$key){
 if(-not $Script:Config.StateResume){ return $false }
 if($Script:State.ContainsKey($key)){ return ($Script:State[$key].status -eq 'SUCCESS') }
 return $false
}

function Stop-IfRequested {
 if([CSNative]::StopPressed()){ throw 'EMERGENCY_STOP_F12' }
}
function RootFromHandle([IntPtr]$h){ return [System.Windows.Automation.AutomationElement]::FromHandle($h) }

# ---- Explorer adapter (proven reader) ---------------------------------------
function Get-ExplorerWindows {
 $a=@()
 foreach($h in [CSNative]::Windows()){
  if([CSNative]::Class($h) -eq 'CabinetWClass'){ $a+=$h }
 }
 return $a
}
function Get-DescendantText($e){
 $parts=@()
 try{
  $d=$e.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  for($i=0;$i -lt $d.Count;$i++){ try{ $n=$d.Item($i).Current.Name; if($n){ $parts+=$n } }catch{} }
 }catch{}
 return ($parts -join ' | ')
}
function Classify-Row($e,$name){
 $ext=[IO.Path]::GetExtension($name).ToLowerInvariant()
 if($ext -in $Script:Config.SupportedExtensions){ return 'OFFICE' }
 $meta=Get-DescendantText $e
 try{ $help=$e.Current.HelpText }catch{ $help='' }
 if(($meta -and $meta.Contains($KFolder)) -or ($help -and $help.Contains($KFolder))){ return 'FOLDER' }
 if([string]::IsNullOrWhiteSpace($ext)){ return 'FOLDER' }
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
   if($type -notin @('ControlType.DataItem','ControlType.ListItem') -or [string]::IsNullOrWhiteSpace($name)){ continue }
   $r=$e.Current.BoundingRectangle
   if($r.Width -le 0 -or $r.Height -le 0){ continue }
   $key=$type+'|'+$name
   if($seen[$key]){ continue };$seen[$key]=$true
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
 if(-not $c){ throw 'No readable File Explorer window found. Open the desired start folder first.' }
 $x=$c|Sort-Object Score -Descending|Select-Object -First 1
 Log ('START_EXPLORER "'+$x.Title+'" items='+$x.Rows.Count+' office='+@($x.Rows|Where-Object Kind -eq 'OFFICE').Count+' folders='+@($x.Rows|Where-Object Kind -eq 'FOLDER').Count)
 return $x.H
}

# ---- Generic UIA helpers ----------------------------------------------------
function Activate-Element($item,[bool]$double=$false){
 Stop-IfRequested
 $e=$item.Element;$p=$null
 if(-not $double -and $e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.InvokePattern]$p).Invoke();return 'InvokePattern'
 }
 $p=$null
 if(-not $double -and $e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.SelectionItemPattern]$p).Select();return 'SelectionItemPattern'
 }
 $x=[int]($item.X+$item.W/2);$y=[int]($item.Y+$item.H/2)
 if($double){ [CSNative]::DoubleClick($x,$y) }else{ [CSNative]::Click($x,$y) }
 return ('UIA_RECT@'+$x+','+$y)
}
function Find-ByName([IntPtr]$h,[string]$name,[bool]$contains,[bool]$requireOnscreen){
 $root=RootFromHandle $h
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n)){ continue }
   if($requireOnscreen -and $e.Current.IsOffscreen){ continue }
   $ok=if($contains){ $n.Contains($name) }else{ $n -eq $name }
   if($ok){
    $r=$e.Current.BoundingRectangle
    $hits += [pscustomobject]@{Name=$n;Id=$e.Current.AutomationId;Type=$e.Current.ControlType.ProgrammaticName;Off=$e.Current.IsOffscreen;Element=$e;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
   }
  }catch{}
 }
 return $hits
}
function Find-ByRegex([IntPtr]$h,[string]$regex){
 $root=RootFromHandle $h
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name;$aid=$e.Current.AutomationId
   $match=$false
   if($n -and $n -match $regex){ $match=$true }
   if($match){
    $r=$e.Current.BoundingRectangle
    $hits += [pscustomobject]@{Name=$n;Id=$aid;Type=$e.Current.ControlType.ProgrammaticName;Off=$e.Current.IsOffscreen;Element=$e;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
   }
  }catch{}
 }
 return $hits
}
function WindowMap {
 $m=@{}
 foreach($h in [CSNative]::Windows()){ $m[[string]$h.ToInt64()]=[CSNative]::Title($h) }
 return $m
}

# ---- Office window adapter --------------------------------------------------
function Find-OfficeWindow([string]$ext,[string]$base,[int]$timeout){
 $deadline=(Get-Date).AddSeconds($timeout)
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  foreach($h in [CSNative]::Windows()){
   $cls=[CSNative]::Class($h);$title=[CSNative]::Title($h)
   if($ext -eq '.pptx' -and $cls -eq 'PPTFrameClass' -and $title -like ('*'+$base+'*')){ return $h }
   if($ext -in @('.xlsx','.xls') -and $cls -eq 'XLMAIN' -and $title -like ('*'+$base+'*')){ return $h }
  }
  Start-Sleep -Milliseconds 300
 }
 return [IntPtr]::Zero
}
function Prepare-OfficeWindow([IntPtr]$office){
 try{
  $root=RootFromHandle $office;$p=$null
  if($root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$p)){
   try{ ([System.Windows.Automation.WindowPattern]$p).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized) }catch{}
  }
 }catch{}
 [CSNative]::SetForegroundWindow($office)|Out-Null
 Start-Sleep -Milliseconds 1200
}
function Try-ExitProtectedView([IntPtr]$office){
 if(-not $Script:Config.EnableProtectedViewEdit){ return }
 $hit=@(Find-ByName $office $KEnableEdit $false $true)
 if($hit.Count -eq 0){ $hit=@(Find-ByName $office 'Enable Editing' $false $true) }
 if($hit.Count -ge 1){
  Log 'PROTECTED_VIEW detected; clicking Enable Editing (standard Office button)'
  $null=Activate-Element $hit[0] $false
  Start-Sleep -Milliseconds 1500
 }
}
function Try-SelectHomeTab([IntPtr]$office){
 # The analyzer group lives on the Home tab; make sure it is active so the
 # add-in ribbon button is present in the UIA tree.
 $root=RootFromHandle $office
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   if($e.Current.ControlType.ProgrammaticName -ne 'ControlType.TabItem'){ continue }
   $n=$e.Current.Name;$aid=$e.Current.AutomationId
   if($aid -eq 'TabHome' -or $n -eq $KHome -or $n -eq 'Home'){
    $p=$null
    if($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p)){
     try{ ([System.Windows.Automation.SelectionItemPattern]$p).Select();Start-Sleep -Milliseconds 400 }catch{}
    }
    return
   }
  }catch{}
 }
}

# ---- Analyzer adapter (proven open logic) -----------------------------------
function Find-AnalyzerCandidates([IntPtr]$office,[string]$ext){
 if($ext -eq '.pptx'){
  $regex='(?i)PPTX\s*analyzer'
 }else{
  $regex='(?i)Excel\s*analyzer'
 }
 $hits=@(Find-ByRegex $office $regex)
 if($hits.Count -eq 0){
  # Looser fallback: the add-in name plus "analy" anywhere.
  if($ext -eq '.pptx'){ $regex='(?i)PPTX.*analy|analy.*PPTX' }else{ $regex='(?i)Excel.*analy|analy.*Excel' }
  $hits=@(Find-ByRegex $office $regex)
 }
 # Prefer onscreen, button-like controls but keep others for diagnostics.
 return $hits
}
function Pick-Analyzer($hits,[string]$label){
 if($hits.Count -eq 0){ return $null }
 $exact=@($hits|Where-Object {$_.Name -eq $label})
 if($exact.Count -ge 1){ return $exact[0] }
 $btn=@($hits|Where-Object {$_.Type -eq 'ControlType.Button'})
 if($btn.Count -ge 1){ return $btn[0] }
 return $hits[0]
}
function Ensure-AnalyzerPane([IntPtr]$office,[string]$ext){
 Prepare-OfficeWindow $office
 Try-ExitProtectedView $office
 if(@(Find-ByName $office $KStart $false $true).Count -gt 0){
  Log 'ANALYZER_PANE already open'
  return
 }
 Try-SelectHomeTab $office

 $label=if($ext -eq '.pptx'){ $Script:Config.PowerPointAnalyzerLabel }else{ $Script:Config.ExcelAnalyzerLabel }
 $deadline=(Get-Date).AddSeconds([Math]::Max(30,$Script:Config.AnalyzerOpenTimeoutSec))
 $target=$null;$lastCount=-1
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  if(@(Find-ByName $office $KStart $false $true).Count -gt 0){
   Log 'ANALYZER_PANE appeared while waiting'
   return
  }
  $hits=@(Find-AnalyzerCandidates $office $ext)
  if($hits.Count -ne $lastCount){
   Log ('ANALYZER_CANDIDATES '+$label+' count='+$hits.Count)
   foreach($hh in $hits){ Log ('  candidate type='+$hh.Type+' name="'+$hh.Name+'" id="'+$hh.Id+'" off='+$hh.Off+' rect='+$hh.X+','+$hh.Y+','+$hh.W+','+$hh.H) }
   $lastCount=$hits.Count
  }
  $target=Pick-Analyzer $hits $label
  if($target){ break }
  # Add-in ribbon commands can load several seconds after the document window.
  [CSNative]::SetForegroundWindow($office)|Out-Null
  Start-Sleep -Milliseconds 700
 }
 if(-not $target){ throw ('Analyzer ribbon button not found after waiting: '+$label) }

 Log ('ANALYZER_OPEN '+$label+' via '+$target.Type)
 $null=Activate-Element $target $false

 $deadline=(Get-Date).AddSeconds([Math]::Max(30,$Script:Config.AnalyzerOpenTimeoutSec))
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  if(@(Find-ByName $office $KStart $false $true).Count -gt 0){
   Log 'ANALYZER_PANE ready'
   return
  }
  Start-Sleep -Milliseconds 300
 }
 throw 'Analyzer task pane did not expose the start button in time.'
}

# ---- Analysis state machine (proven cached/interactive handling) ------------
function Run-Analysis([IntPtr]$office,[string]$logical){
 $ext=[IO.Path]::GetExtension($logical).ToLowerInvariant()
 Ensure-AnalyzerPane $office $ext
 $start=@(Find-ByName $office $KStart $false $true)
 if($start.Count -lt 1){ throw 'Analysis start button not visible.' }
 $baseline=WindowMap
 $null=Activate-Element $start[0] $false
 Log ('ANALYSIS_START '+$logical)

 $deadline=(Get-Date).AddSeconds($Script:Config.AnalysisTimeoutSec)
 $seenWorking=$false;$seenAuth=$false;$reportedAuth=@{}
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  $startVisible=@(Find-ByName $office $KStart $false $true).Count -gt 0
  $busyVisible =@(Find-ByName $office $KBusy  $false $true).Count -gt 0
  $doneVisible =@(Find-ByName $office $KDone  $true  $true).Count -gt 0
  $stopVisible =@(Find-ByName $office $KStopped $true $true).Count -gt 0

  if($busyVisible){ if(-not $seenWorking){ Log 'ANALYZING' }; $seenWorking=$true }

  # Detect a new sign-in / consent window the user must complete.
  $now=WindowMap
  foreach($k in $now.Keys){
   if(-not $baseline.ContainsKey($k) -and -not $reportedAuth[$k]){
    $t=$now[$k]
    if($t -and $t -notmatch 'PowerShell'){
     Log ('INTERACTIVE_WINDOW "'+$t+'" - complete sign-in/consent manually if requested; no password is stored')
     $reportedAuth[$k]=$true;$seenAuth=$true
    }
   }
  }

  if($doneVisible){ Log ('ANALYSIS_DONE '+$logical+' (report text detected)'); return }
  if($stopVisible){ throw ('Analyzer reported a stop/error state for '+$logical) }

  # The add-in restores the start button in finally{} after success or failure.
  # If we saw an active phase (busy or auth) and the start button returns, treat
  # that as cycle completion even when WebView status text is not UIA-exposed.
  if($startVisible -and ($seenWorking -or $seenAuth)){
   Log ('ANALYSIS_FINISHED '+$logical+' (start button returned)')
   return
  }
  Start-Sleep -Milliseconds 400
 }
 throw 'Analysis timeout. A sign-in/consent window may still be waiting.'
}
function Close-Office([IntPtr]$h){
 try{
  $root=RootFromHandle $h;$p=$null
  if($root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$p)){
   ([System.Windows.Automation.WindowPattern]$p).Close()
  }else{ throw 'No WindowPattern' }
 }catch{ Log ('WARN close failed: '+$_.Exception.Message);return }
 $deadline=(Get-Date).AddSeconds($Script:Config.CloseTimeoutSec)
 while((Get-Date)-lt $deadline){
  $exists=$false
  foreach($x in [CSNative]::Windows()){ if($x -eq $h){ $exists=$true;break } }
  if(-not $exists){ Log 'OFFICE_CLOSE';return }
  Start-Sleep -Milliseconds 300
 }
 Log 'WARN Office window still open after close timeout; continuing without force-kill.'
}

# ---- Per-file processing ----------------------------------------------------
function Process-OfficeItem([IntPtr]$explorer,$item,[string]$logical){
 if(Is-Completed $logical){ Log ('SKIP_DONE '+$logical);return }
 if($Script:Config.MaxOfficeFiles -gt 0 -and $Script:OfficeCount -ge $Script:Config.MaxOfficeFiles){
  Log ('SKIP_CAP '+$logical+' (maxOfficeFiles='+$Script:Config.MaxOfficeFiles+')');return
 }
 $ext=[IO.Path]::GetExtension($item.Name).ToLowerInvariant()
 $base=[IO.Path]::GetFileNameWithoutExtension($item.Name)
 Log ('OPEN '+$logical)
 if($Script:Config.DryRun){ Record-State $logical 'DRYRUN' $ext '';$Script:OfficeCount++;return }
 Record-State $logical 'OPENING' $ext ''
 $null=Activate-Element $item $true
 $office=Find-OfficeWindow $ext $base $Script:Config.OfficeOpenTimeoutSec
 if($office -eq [IntPtr]::Zero){ throw ('Office window timeout for '+$item.Name) }
 Log ('OFFICE_READY '+(&{ if($ext -eq '.pptx'){'PowerPoint'}else{'Excel'} }))
 try{
  Run-Analysis $office $logical
  Record-State $logical 'SUCCESS' $ext ''
  Log ('SUCCESS '+$logical)
  $Script:OfficeCount++
 }catch{
  Record-State $logical 'FAILED' $ext $_.Exception.Message
  throw
 }finally{
  Close-Office $office
 }
}

# ---- Explorer navigation ----------------------------------------------------
function Click-Back([IntPtr]$explorer){
 $hits=@(Find-ByName $explorer $KBack $false $true)
 if($hits.Count -eq 0){ $hits=@(Find-ByName $explorer 'Back' $false $true) }
 if($hits.Count -eq 0){ throw 'Explorer Back button not found.' }
 $null=Activate-Element $hits[0] $false
 Start-Sleep -Milliseconds 800
}
function Enter-Folder([IntPtr]$explorer,$item){
 $before=[CSNative]::Title($explorer)
 $null=Activate-Element $item $true
 $deadline=(Get-Date).AddSeconds(10)
 while((Get-Date)-lt $deadline){
  Stop-IfRequested
  if([CSNative]::Title($explorer) -ne $before){ return }
  Start-Sleep -Milliseconds 250
 }
 # Some Explorer tabs keep a generic title; give the view a moment to settle.
 Start-Sleep -Milliseconds 600
}
function Walk-Folder([IntPtr]$explorer,[string]$logical,[int]$depth){
 Stop-IfRequested
 if($depth -gt $Script:Config.MaxDepth){ Log ('SKIP_DEPTH '+$logical);return }
 $rows=@(Read-ExplorerRows $explorer)
 Log ('SCAN '+$logical+' items='+$rows.Count+' office='+@($rows|Where-Object Kind -eq 'OFFICE').Count+' folders='+@($rows|Where-Object Kind -eq 'FOLDER').Count)

 # Process Office files first so folder navigation does not invalidate elements.
 foreach($r in @($rows|Where-Object Kind -eq 'OFFICE')){
  Stop-IfRequested
  if($Script:Config.MaxOfficeFiles -gt 0 -and $Script:OfficeCount -ge $Script:Config.MaxOfficeFiles){ break }
  $key=if($logical){ $logical+'\'+$r.Name }else{ $r.Name }
  try{ Process-OfficeItem $explorer $r $key }catch{
   if($_.Exception.Message -eq 'EMERGENCY_STOP_F12'){ throw }
   Log ('ERROR '+$key+' :: '+$_.Exception.Message)
  }
 }

 if(-not $Script:Config.EnableRecursion){ return }
 if($Script:Config.MaxOfficeFiles -gt 0 -and $Script:OfficeCount -ge $Script:Config.MaxOfficeFiles){ return }

 $folderNames=@($rows|Where-Object Kind -eq 'FOLDER'|ForEach-Object {$_.Name})
 foreach($fname in $folderNames){
  Stop-IfRequested
  $fresh=@(Read-ExplorerRows $explorer|Where-Object {$_.Kind -eq 'FOLDER' -and $_.Name -eq $fname}|Select-Object -First 1)
  if(-not $fresh){ Log ('WARN folder disappeared: '+$fname);continue }
  $child=if($logical){ $logical+'\'+$fname }else{ $fname }
  try{
   Log ('ENTER '+$child)
   Enter-Folder $explorer $fresh[0]
   Walk-Folder $explorer $child ($depth+1)
   Click-Back $explorer
   Log ('BACK '+$logical)
  }catch{
   if($_.Exception.Message -eq 'EMERGENCY_STOP_F12'){ throw }
   Log ('ERROR_FOLDER '+$child+' :: '+$_.Exception.Message)
   try{ Click-Back $explorer }catch{}
  }
 }
}

# ---- Main -------------------------------------------------------------------
Write-Host ''
Write-Host 'CloudSave Full UI Agent v3.0'
Write-Host 'Explorer (UI Automation) -> Office -> Analyzer -> analysis -> close -> recurse.'
Write-Host ('Recursion: '+$Script:Config.EnableRecursion+'  MaxOfficeFiles: '+$Script:Config.MaxOfficeFiles+'  Resume: '+$Script:Config.StateResume)
Write-Host 'Emergency stop: press F12 at any time.'
Write-Host 'IMPORTANT: open the desired START folder in File Explorer before running.'
Write-Host ''

Load-State
$explorer=Select-StartExplorer
Log ('RUN_START log='+$Script:LogFile)
try{
 Walk-Folder $explorer '' 0
 Log ('RUN_COMPLETE processed='+$Script:OfficeCount)
}catch{
 if($_.Exception.Message -eq 'EMERGENCY_STOP_F12'){
  Log 'RUN_ABORT emergency stop (F12)'
 }else{
  Log ('RUN_ABORT '+$_.Exception.Message)
 }
}
$succ=@($Script:State.Values|Where-Object {$_.status -eq 'SUCCESS'}).Count
$fail=@($Script:State.Values|Where-Object {$_.status -eq 'FAILED'}).Count
Write-Host ''
Write-Host ('Finished. This run processed: '+$Script:OfficeCount+' Office file(s).')
Write-Host ('State DB totals -> SUCCESS='+$succ+'  FAILED='+$fail)
Write-Host ('Log:   '+$Script:LogFile)
Write-Host ('State: '+$Script:StateFile)
