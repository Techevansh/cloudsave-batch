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
function Invoke-El($el){
 $p=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$p)){([System.Windows.Automation.InvokePattern]$p).Invoke();return $true}
 return $false
}
function Dump-MenuCandidates {
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name;$aid=$e.Current.AutomationId;$cls=$e.Current.ClassName;$ct=$e.Current.ControlType.ProgrammaticName
   if([string]::IsNullOrWhiteSpace($n)){continue}
   if($n -match 'Cloud|Save|추가 기능|내 추가 기능|Office' -or $aid -match 'Cloud|Addin|Extension'){
    $r=$e.Current.BoundingRectangle
    $rows += [pscustomobject]@{Element=$e;N=$n;A=$aid;C=$cls;T=$ct;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
   }
  }catch{}
 }
 return $rows
}

Write-Host ''
Write-Host 'CloudSave Add-in Menu Probe v0.8'
Write-Host 'GUARDED ACTION: opens PowerPoint Add-ins menu only.'
Write-Host 'It will NOT click CloudSave and will NOT save/upload anything.'
Write-Host ''

$wins=@(Get-Ppt)
if(!$wins){throw 'No PowerPoint window found.'}
$target=$wins | Where-Object {$_.Current.Name -match 'Summary'} | Select-Object -First 1
if(!$target){$target=$wins | Select-Object -First 1}
Write-Host ('PowerPoint: '+$target.Current.Name)

$button=Find-ById $target 'OfficeExtensionsShowAddinFlyout'
if(!$button){throw 'PowerPoint Add-ins button was not found by AutomationId.'}
Write-Host ('Found Add-ins button: '+$button.Current.Name+' | id='+$button.Current.AutomationId)
Write-Host 'Opening Add-ins menu through UI Automation InvokePattern...'
if(!(Invoke-El $button)){throw 'Add-ins button does not expose InvokePattern.'}
Start-Sleep -Milliseconds 1200

$candidates=@(Dump-MenuCandidates)
Write-Host ('Menu/add-in candidates found: '+$candidates.Count)
$i=0
foreach($r in $candidates){
 $i++
 Write-Host ('['+$i+'] '+$r.T+' | name="'+$r.N+'" | id="'+$r.A+'" | class="'+$r.C+'" | rect='+$r.X+','+$r.Y+','+$r.W+','+$r.H)
}
$cloud=@($candidates | Where-Object {$_.N -match 'CloudSave|Cloud Save'})
if($cloud.Count){
 Write-Host ''
 Write-Host ('SUCCESS: CloudSave candidate detected: '+$cloud[0].N)
 Write-Host 'SAFE STOP: CloudSave was NOT clicked.'
}else{
 Write-Host ''
 Write-Host 'RESULT: Add-ins menu opened, but no named CloudSave candidate is exposed yet.'
 Write-Host 'SAFE STOP: no further UI action was performed.'
}
