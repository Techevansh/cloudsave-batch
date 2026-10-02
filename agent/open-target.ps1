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
function ClickPoint([int]$x,[int]$y){
 [M]::SetCursorPos($x,$y)|Out-Null; Start-Sleep -Milliseconds 200
 [M]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
$label=D $DriveLabelBase64
Write-Host ''; Write-Host 'CloudSave UI Agent v0.6.1'; Write-Host ('Visible target: '+$label)
Start-Process explorer.exe; Start-Sleep -Seconds 2
$w=New-Object -ComObject WScript.Shell
$e=Get-Process explorer -ErrorAction SilentlyContinue|?{$_.MainWindowHandle -ne 0}|select -First 1
if($null -eq $e){Write-Host 'ERROR: Explorer not found.';exit 3}
if(-not $w.AppActivate($e.Id)){Write-Host 'ERROR: Explorer activation failed.';exit 4}
Start-Sleep -Milliseconds 700
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$r=[Windows.Automation.AutomationElement]::FromHandle($e.MainWindowHandle)
$all=$r.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
$candidates=@()
for($n=0;$n -lt $all.Count;$n++){
 $el=$all.Item($n)
 try{$name=$el.Current.Name;$rect=$el.Current.BoundingRectangle
  if((-not [string]::IsNullOrWhiteSpace($name))-and(-not $rect.IsEmpty)-and(($name -like '*U:*')-or($name -like '*VTW*'))){$candidates+=$el}
 }catch{}
}
Write-Host ('UI candidates found: '+$candidates.Count)
foreach($el in $candidates){
 try{$name=$el.Current.Name;$rect=$el.Current.BoundingRectangle;$x=[int]($rect.X+$rect.Width/2);$y=[int]($rect.Y+$rect.Height/2)
  Write-Host ('Candidate: '+$name);ClickPoint $x $y;Write-Host ('OK: clicked visible candidate: '+$name);exit 0
 }catch{}
}
$wr=$r.Current.BoundingRectangle
$x=[int]($wr.X+110);$y=[int]($wr.Y+610)
Write-Host 'WARN: UIA did not expose U drive. Trying Explorer-relative fallback.'
ClickPoint $x $y
Write-Host ('OK: fallback click sent at '+$x+','+$y)
