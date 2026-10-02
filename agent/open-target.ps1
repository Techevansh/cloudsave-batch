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
function Click([int]$x,[int]$y,[int]$count=1){
 [M]::SetCursorPos($x,$y)|Out-Null;Start-Sleep -Milliseconds 250
 for($i=0;$i -lt $count;$i++){[M]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[M]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 120}
}
Write-Host '';Write-Host 'CloudSave UI Agent v0.6.2'
Start-Process explorer.exe;Start-Sleep -Seconds 2
$w=New-Object -ComObject WScript.Shell
$e=Get-Process explorer -ErrorAction SilentlyContinue|?{$_.MainWindowHandle -ne 0}|select -First 1
if($null -eq $e){Write-Host 'ERROR: Explorer not found.';exit 3}
$w.AppActivate($e.Id)|Out-Null;Start-Sleep -Milliseconds 700
Add-Type -AssemblyName UIAutomationClient;Add-Type -AssemblyName UIAutomationTypes
$r=[Windows.Automation.AutomationElement]::FromHandle($e.MainWindowHandle)
$wr=$r.Current.BoundingRectangle

# Windows 11 Explorer navigation pane geometry.
# Previous run proved the pane is stable but the old fallback Y landed on another item.
# Click U: relative to the Explorer window using the visible navigation layout.
$x=[int]($wr.X+85)
$y=[int]($wr.Y+585)
Write-Host ('Clicking visible U drive position at '+$x+','+$y)
Click $x $y
Start-Sleep -Seconds 3

# After U opens, use visible UI only. Try to find the first target folder by UIA.
$folder='부서 폴더'
$all=$r.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
for($n=0;$n -lt $all.Count;$n++){
 try{
  $el=$all.Item($n);$name=$el.Current.Name;$rect=$el.Current.BoundingRectangle
  if(($name -eq $folder)-and(-not $rect.IsEmpty)){
   $fx=[int]($rect.X+$rect.Width/2);$fy=[int]($rect.Y+$rect.Height/2)
   Write-Host 'U drive opened. Found department folder in visible UI.'
   Click $fx $fy 2
   Write-Host 'OK: opened department folder.'
   exit 0
  }
 }catch{}
}
Write-Host 'OK: U drive click sent.'
Write-Host 'Department folder was not exposed through UIA yet; check Explorer result.'
