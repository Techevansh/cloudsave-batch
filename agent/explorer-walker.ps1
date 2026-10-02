$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WalkerWin {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public static IntPtr Explorer(){
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h,IntPtr l){
   if(!IsWindowVisible(h)) return true;
   var c=new StringBuilder(128); GetClassName(h,c,c.Capacity);
   if(c.ToString()=="CabinetWClass"){found=h;return false;} return true;
  },IntPtr.Zero); return found;
 }
 public static string Title(IntPtr h){var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
}
'@

function Key([string]$keys,[int]$wait=350){
 [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds $wait
}
function Active-Explorer {
 $h=[WalkerWin]::Explorer()
 if($h -eq [IntPtr]::Zero){throw 'No File Explorer window is open. Open the START folder first.'}
 [WalkerWin]::SetForegroundWindow($h)|Out-Null
 Start-Sleep -Milliseconds 500
 return $h
}

Write-Host ''
Write-Host 'CloudSave Explorer Walker v0.1'
Write-Host 'SAFE DISCOVERY MODE - it will NOT open files or folders.'
Write-Host 'Open the desired START folder in File Explorer before running.'
Write-Host ''

$h=Active-Explorer
$title=[WalkerWin]::Title($h)
Write-Host ('Explorer detected: '+$title)
Write-Host 'Reading visible item names by keyboard clipboard navigation...'

# Focus Explorer item pane without needing U: filesystem access.
Key '^l' 200
Key '{ESC}' 200
Key '^a' 150
# F6 cycles Explorer UI zones; TAB then first-item selection is more stable than mouse coordinates.
Key '{F6}' 150
Key '{F6}' 150
Key '{F6}' 150
Key '{HOME}' 200

$seen=New-Object System.Collections.Generic.List[string]
$last=''
for($i=0;$i -lt 250;$i++){
 Key '^c' 180
 $clip=''
 try{$clip=[System.Windows.Forms.Clipboard]::GetText()}catch{}
 $clip=($clip -replace "[\r\n]+"," ").Trim()
 if($clip -and $clip -ne $last){
   $seen.Add($clip); Write-Host ('  ['+$seen.Count+'] '+$clip)
   $last=$clip
 }
 Key '{DOWN}' 120
 if($seen.Count -gt 1 -and $clip -eq $seen[0]){break}
}
Write-Host ''
Write-Host ('Captured selections: '+$seen.Count)
Write-Host 'SAFE STOP: discovery only. No Enter/double-click/CloudSave action was performed.'
Write-Host 'If names are captured correctly, the next version will classify folders/PPTX/XLS/XLSX and walk them.'
