$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-Ppt {
 $d=[System.Windows.Automation.AutomationElement]::RootElement
 $wins=$d.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
 $a=@()
 for($i=0;$i -lt $wins.Count;$i++){ $w=$wins.Item($i); try{if($w.Current.ClassName -eq 'PPTFrameClass'){$a+=$w}}catch{} }
 return $a
}
function Dump([System.Windows.Automation.AutomationElement]$root){
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name;$aid=$e.Current.AutomationId;$cls=$e.Current.ClassName;$ct=$e.Current.ControlType.ProgrammaticName
   if([string]::IsNullOrWhiteSpace($n) -and [string]::IsNullOrWhiteSpace($aid)){continue}
   $r=$e.Current.BoundingRectangle
   $rows += [pscustomobject]@{N=$n;A=$aid;C=$cls;T=$ct;X=[int]$r.X;Y=[int]$r.Y;W=[int]$r.Width;H=[int]$r.Height}
  }catch{}
 }
 return $rows
}
Write-Host ''
Write-Host 'CloudSave PowerPoint Add-in Surface Probe v0.7.1'
Write-Host 'READ-ONLY. No click, no keyboard, no CloudSave execution.'
Write-Host ''
$wins=@(Get-Ppt)
if(!$wins){throw 'No PowerPoint window found.'}
$rx='Cloud|Save|Add-in|Office Add'
foreach($w in $wins){
 Write-Host ('WINDOW: '+$w.Current.Name)
 $rows=@(Dump $w)
 Write-Host ('UI elements: '+$rows.Count)
 $interesting=@($rows | Where-Object { ($_.N -match $rx) -or ($_.A -match 'Cloud|Addin|TaskPane|WebView') -or ($_.C -match 'WebView|Internet Explorer|Chrome') })
 if($interesting.Count){
  Write-Host '--- add-in/save/cloud candidates ---'
  $i=0;foreach($r in $interesting){$i++;Write-Host ('['+$i+'] '+$r.T+' | name="'+$r.N+'" | id="'+$r.A+'" | class="'+$r.C+'" | rect='+$r.X+','+$r.Y+','+$r.W+','+$r.H)}
 }else{Write-Host 'No named add-in/cloud candidate exposed.'}
 Write-Host '--- non-empty controls near PowerPoint chrome (first 140) ---'
 $i=0
 foreach($r in ($rows|Where-Object {$_.Y -ge 0 -and $_.Y -lt 500}|Select-Object -First 140)){$i++;Write-Host ('['+$i+'] '+$r.T+' | "'+$r.N+'" | id="'+$r.A+'" | '+$r.X+','+$r.Y+','+$r.W+','+$r.H)}
}
Write-Host ''
Write-Host 'RESULT: probe completed. Nothing was activated.'
