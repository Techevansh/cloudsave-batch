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

function Key([string]$keys,[int]$wait=250){
 [System.Windows.Forms.SendKeys]::SendWait($keys)
 Start-Sleep -Milliseconds $wait
}
function Active-Explorer {
 $h=[WalkerWin]::Explorer()
 if($h -eq [IntPtr]::Zero){throw 'No File Explorer window is open. Open the START folder first.'}
 [WalkerWin]::SetForegroundWindow($h)|Out-Null
 Start-Sleep -Milliseconds 500
 return $h
}

Write-Host ''
Write-Host 'CloudSave Explorer Walker v0.2'
Write-Host 'SAFE SINGLE-SELECTION TEST - no Ctrl+A, no file/folder open.'
Write-Host 'IMPORTANT: while this test runs, do not type or click.'
Write-Host ''

$h=Active-Explorer
$title=[WalkerWin]::Title($h)
Write-Host ('Explorer detected: '+$title)
Write-Host 'Selecting ONE item at a time. Clipboard is cleared before every read.'

# Never use Ctrl+A. Move focus among Explorer zones, then Home selects only one row.
Key '{F6}' 150
Key '{F6}' 150
Key '{F6}' 150
Key '{HOME}' 250

$seen=New-Object System.Collections.Generic.List[string]
$duplicates=0
for($i=0;$i -lt 80;$i++){
 try{[System.Windows.Forms.Clipboard]::Clear()}catch{}
 Key '^c' 220
 $clip=''
 try{$clip=[System.Windows.Forms.Clipboard]::GetText()}catch{}
 $clip=($clip -replace "[\r\n]+"," ").Trim()

 if($clip){
   if($seen.Count -eq 0 -or $clip -ne $seen[$seen.Count-1]){
     $seen.Add($clip)
     Write-Host ('  ['+$seen.Count+'] '+$clip)
     $duplicates=0
   } else {
     $duplicates++
   }
 } else {
   $duplicates++
 }

 if($duplicates -ge 3){break}
 Key '{DOWN}' 160
}

Write-Host ''
Write-Host ('Captured single selections: '+$seen.Count)
Write-Host 'SAFE STOP: no Enter, double-click, or CloudSave action was performed.'
Write-Host 'This version intentionally removed Ctrl+A because it selected every Explorer item at once.'
