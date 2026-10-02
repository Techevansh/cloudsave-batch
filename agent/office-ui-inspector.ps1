$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-OfficeWindows {
 $desktop=[System.Windows.Automation.AutomationElement]::RootElement
 $wins=$desktop.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
 $out=@()
 for($i=0;$i -lt $wins.Count;$i++){
  $w=$wins.Item($i)
  try{
   $cls=$w.Current.ClassName;$name=$w.Current.Name
   if($cls -in @('PPTFrameClass','XLMAIN') -or $name -match 'PowerPoint|Excel'){
    $out += $w
   }
  }catch{}
 }
 return $out
}

function Read-Actionable([System.Windows.Automation.AutomationElement]$root){
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@();$seen=@{}
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $name=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($name)){continue}
   $type=$e.Current.ControlType.ProgrammaticName
   $key=$type+'|'+$name
   if($seen[$key]){continue};$seen[$key]=$true
   $patterns=@()
   foreach($p in @(
    [System.Windows.Automation.InvokePattern]::Pattern,
    [System.Windows.Automation.SelectionItemPattern]::Pattern,
    [System.Windows.Automation.LegacyIAccessiblePattern]::Pattern,
    [System.Windows.Automation.TogglePattern]::Pattern,
    [System.Windows.Automation.ExpandCollapsePattern]::Pattern
   )){
    $obj=$null
    if($e.TryGetCurrentPattern($p,[ref]$obj)){$patterns += $p.ProgrammaticName}
   }
   if($patterns.Count -gt 0){
    $rows += [pscustomobject]@{Name=$name;Type=$type;Patterns=($patterns -join ', ')}
   }
  }catch{}
 }
 return $rows
}

Write-Host ''
Write-Host 'CloudSave Office UI Inspector v0.6'
Write-Host 'READ-ONLY: inspects the already-open PowerPoint/Excel window.'
Write-Host 'No keyboard input. No mouse clicks. CloudSave will NOT run.'
Write-Host ''

$wins=@(Get-OfficeWindows)
if($wins.Count -eq 0){throw 'No open PowerPoint/Excel window was found. Keep the file opened by v0.5 and retry.'}
Write-Host ('Office windows found: '+$wins.Count)
foreach($w in $wins){
 Write-Host ''
 Write-Host ('WINDOW: '+$w.Current.Name)
 Write-Host ('CLASS:  '+$w.Current.ClassName)
 $rows=@(Read-Actionable $w)
 Write-Host ('Actionable UI elements: '+$rows.Count)
 $cloud=@($rows|Where-Object {$_.Name -match 'Cloud.?Save|CloudSave|클라우드'})
 if($cloud.Count -gt 0){
  Write-Host '--- CloudSave-related candidates ---'
  $n=0
  foreach($r in $cloud){$n++;Write-Host ('  ['+$n+'] '+$r.Type+' | '+$r.Name+' | '+$r.Patterns)}
 }else{
  Write-Host 'CloudSave-related candidate: NOT EXPOSED in the current Office UI tree.'
  Write-Host 'This is diagnostic only; nothing will be clicked.'
 }
 Write-Host '--- Useful ribbon/add-in candidates (first 80) ---'
 $n=0
 foreach($r in ($rows|Select-Object -First 80)){$n++;Write-Host ('  ['+$n+'] '+$r.Type+' | '+$r.Name+' | '+$r.Patterns)}
}
Write-Host ''
Write-Host 'SAFE STOP: inspection completed. No action was performed.'
