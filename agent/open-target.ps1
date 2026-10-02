param([string]$DriveLabelBase64)
$ErrorActionPreference='Stop'
function D([string]$v){[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($v))}
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class M {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int X,int Y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@
$label=D $DriveLabelBase64
Write-Host ''
Write-Host 'CloudSave UI Agent v0.6'
Write-Host ('Visible target: '+$label)
Start-Process explorer.exe
Start-Sleep -Seconds 2
$w=New-Object -ComObject WScript.Shell
$e=Get-Process explorer -ErrorAction SilentlyContinue|?{$_.MainWindowHandle -ne 0}|select -First 1
if($null -eq $e){Write-Host 'ERROR: Explorer not found.';exit 3}
if(-not $w.AppActivate($e.Id)){Write-Host 'ERROR: Explorer activation failed.';exit 4}
Start-Sleep -Milliseconds 500
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$r=[Windows.Automation.AutomationElement]::FromHandle($e.MainWindowHandle)
$c=New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,$label)
$i=$r.FindFirst([Windows.Automation.TreeScope]::Descendants,$c)
if($null -eq $i){Write-Host 'ERROR: visible U drive item not found.';exit 5}
$p=$null
if($i.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$p)){
 $p.Invoke(); Write-Host 'OK: U drive invoked through UI Automation.';exit 0
}
$b=$i.Current.BoundingRectangle
if($b.IsEmpty){Write-Host 'ERROR: U drive has no clickable rectangle.';exit 6}
$x=[int]($b.X+$b.Width/2);$y=[int]($b.Y+$b.Height/2)
[M]::SetCursorPos($x,$y)|Out-Null
Start-Sleep -Milliseconds 150
[M]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[M]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Write-Host ('OK: clicked U drive at '+$x+','+$y)
