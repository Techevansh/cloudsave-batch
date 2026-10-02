$ErrorActionPreference='Stop'
Write-Host ''
Write-Host 'CloudSave Explorer UI Inspector v0.7.1'
Write-Host 'Move the mouse over VTW Server (U:) within 10 seconds. Do not click.'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
}
'@
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
for($i=10;$i -ge 1;$i--){ Write-Host ('Capture in '+$i+' seconds...'); Start-Sleep -Seconds 1 }
$p=New-Object Win32+POINT
[Win32]::GetCursorPos([ref]$p)|Out-Null
Write-Host ('Mouse: '+$p.X+','+$p.Y)
try {
 $point=New-Object Windows.Point($p.X,$p.Y)
 $el=[Windows.Automation.AutomationElement]::FromPoint($point)
 if($null -eq $el){ throw 'No UI Automation element at mouse position.' }
 Write-Host ('Name: '+$el.Current.Name)
 Write-Host ('ControlType: '+$el.Current.ControlType.ProgrammaticName)
 Write-Host ('AutomationId: '+$el.Current.AutomationId)
 Write-Host ('ClassName: '+$el.Current.ClassName)
 Write-Host ('FrameworkId: '+$el.Current.FrameworkId)
 $rect=$el.Current.BoundingRectangle
 Write-Host ('Rectangle: '+[int]$rect.X+','+[int]$rect.Y+' '+[int]$rect.Width+'x'+[int]$rect.Height)
 Write-Host 'Parents:'
 $walker=[Windows.Automation.TreeWalker]::ControlViewWalker
 $cur=$el
 for($n=0;$n -lt 8;$n++){
  $cur=$walker.GetParent($cur); if($null -eq $cur){break}
  Write-Host ('['+$n+'] Name='+$cur.Current.Name+' | Type='+$cur.Current.ControlType.ProgrammaticName+' | Id='+$cur.Current.AutomationId+' | Class='+$cur.Current.ClassName)
 }
} catch { Write-Host ('ERROR: '+$_.Exception.Message) }
Write-Host 'Inspector finished.'