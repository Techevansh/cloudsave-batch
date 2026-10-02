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

function Find-VisibleExactName([string]$targetName){
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $hits=@()
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $n=$e.Current.Name
   if([string]::IsNullOrWhiteSpace($n) -or $e.Current.IsOffscreen){continue}
   if($n -eq $targetName){
    $r=$e.Current.BoundingRectangle
    if($r.Width -gt 0 -and $r.Height -gt 0){
      $hits += [pscustomobject]@{
        Element=$e
        Name=$n
        Type=$e.Current.ControlType.ProgrammaticName
        Id=$e.Current.AutomationId
        X=[int]$r.X
        Y=[int]$r.Y
        W=[int]$r.Width
        H=[int]$r.Height
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
 $x=[int]($hit.X+$hit.W/2)
 $y=[int]($hit.Y+$hit.H/2)
 [SafeUiClick]::Click($x,$y)
 return ('Exact UIA rectangle click @ '+$x+','+$y)
}

# Build the Korean button label from Unicode code points so Windows PowerShell 5.1
# never has to parse Korean source text from a UTF-8-without-BOM Git checkout.
$targetName = -join @(
 [char]0xAD6C,[char]0xC870,[char]0x20,
 [char]0xBD84,[char]0xC11D,[char]0x20,
 [char]0xC2DC,[char]0xC791
)

Write-Host ''
Write-Host 'CloudSave Single PPTX Structure Analysis v1.0.1'
Write-Host 'GUARDED: exactly one visible task-pane analysis button may be activated.'
Write-Host 'No Explorer traversal. No second Office file. No loop.'
Write-Host ''

$buttons=@(Find-VisibleExactName $targetName)
Write-Host ('Visible target candidates: '+$buttons.Count)
$i=0
foreach($b in $buttons){
 $i++
 Write-Host ('['+$i+'] '+$b.Type+' | name="'+$b.Name+'" | id="'+$b.Id+'" | rect='+$b.X+','+$b.Y+','+$b.W+','+$b.H)
}
if($buttons.Count -ne 1){
 throw ('Expected exactly one visible target button, found '+$buttons.Count+'. Nothing was clicked.')
}

$method=Activate $buttons[0]
Write-Host ('Activated using: '+$method)
Write-Host 'Waiting 15 seconds so the task pane can update or open sign-in UI...'
Start-Sleep -Seconds 15
Write-Host ''
Write-Host 'SAFE STOP: one analysis activation was attempted. No next file will be opened.'
Write-Host 'Check the PowerPoint task pane and any sign-in dialog, then send a screenshot.'
