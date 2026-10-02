$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-Ppt {
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
 $a=@()
 for($i=0;$i -lt $wins.Count;$i++){ $w=$wins.Item($i); try{if($w.Current.ClassName -eq 'PPTFrameClass'){$a+=$w}}catch{} }
 return $a
}
function Find-ById($root,$id){
 $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
 return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
}
function Activate-El($el){
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.InvokePattern]$p).Invoke(); return 'InvokePattern'
 }
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.ExpandCollapsePattern]$p).Expand(); return 'ExpandCollapsePattern'
 }
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.LegacyIAccessiblePattern]$p).DoDefaultAction(); return 'LegacyIAccessiblePattern'
 }
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p)){
  ([System.Windows.Automation.SelectionItemPattern]$p).Select(); return 'SelectionItemPattern'
 }
 return $null
}
function Get-VisibleNamedElements {
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n) -or $e.Current.IsOffscreen){continue}
   $r=$e.Current.BoundingRectangle
   if($r.Width -le 0 -or $r.Height -le 0){continue}
   $rows += [pscustomobject]@{N=$n;A=$e.Current.AutomationId;C=$e.Current.ClassName;T=$e.Current.ControlType.ProgrammaticName;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
  }catch{}
 }
 return $rows
}

Write-Host ''
Write-Host 'CloudSave PPTX Analyzer Menu Probe v0.8.2'
Write-Host 'TARGET: the PPTX analyzer button shown in the Document Tools area.'
Write-Host 'GUARDED ACTION: opens PowerPoint Add-ins menu only; PPTX analyzer itself is NOT clicked.'
Write-Host ''

$wins=@(Get-Ppt)
if(!$wins){throw 'No PowerPoint window found.'}
$target=$wins | Where-Object {$_.Current.Name -match 'Summary'} | Select-Object -First 1
if(!$target){$target=$wins | Select-Object -First 1}
Write-Host ('PowerPoint: '+$target.Current.Name)

$button=Find-ById $target 'OfficeExtensionsShowAddinFlyout'
if(!$button){throw 'PowerPoint Add-ins button was not found by AutomationId.'}
Write-Host ('Found Add-ins button: '+$button.Current.Name+' | id='+$button.Current.AutomationId)
$activation=Activate-El $button
if(!$activation){
 Write-Host 'RESULT: Add-ins button exposes none of the supported UI Automation action patterns.'
 Write-Host 'SAFE STOP: no mouse/keyboard fallback was used.'
 exit 2
}
Write-Host ('Opened Add-ins menu using: '+$activation)
Start-Sleep -Milliseconds 1200

$rows=@(Get-VisibleNamedElements)
$wanted=@($rows | Where-Object {$_.N -match 'PPTX|analy|문서도구|CloudSave|Cloud Save|추가 기능|Office'})
Write-Host ('Relevant visible candidates: '+$wanted.Count)
$i=0
foreach($r in $wanted){$i++;Write-Host ('['+$i+'] '+$r.T+' | name="'+$r.N+'" | id="'+$r.A+'" | class="'+$r.C+'" | rect='+$r.X+','+$r.Y+','+$r.W+','+$r.H)}

$pptx=@($rows | Where-Object {$_.N -match 'PPTX.*analy|analy.*PPTX|PPTX'})
Write-Host ''
if($pptx.Count){
 Write-Host ('SUCCESS: PPTX analyzer candidate detected: '+$pptx[0].N)
 Write-Host 'SAFE STOP: PPTX analyzer was NOT clicked.'
}else{
 Write-Host 'RESULT: menu opened, but the PPTX analyzer name is not exposed in the expanded UI yet.'
 Write-Host 'SAFE STOP: no further UI action was performed.'
}
