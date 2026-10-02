$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WalkerNative {
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

Write-Host ''
Write-Host 'CloudSave Explorer Reader v0.3'
Write-Host 'READ-ONLY UI Automation probe'
Write-Host 'No Ctrl+A / arrows / F6 / Enter / mouse clicks.'
Write-Host ''

$h=[WalkerNative]::Explorer()
if($h -eq [IntPtr]::Zero){ throw 'No File Explorer window is open. Open the target folder first.' }
[WalkerNative]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 300
$title=[WalkerNative]::Title($h)
Write-Host ('Explorer detected: '+$title)

# Use Windows UI Automation to inspect the Explorer window without filesystem access
# and without injecting keyboard/mouse input.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::FromHandle($h)
$all=$root.FindAll(
 [System.Windows.Automation.TreeScope]::Descendants,
 [System.Windows.Automation.Condition]::TrueCondition
)

$items=New-Object System.Collections.Generic.List[object]
for($i=0;$i -lt $all.Count;$i++){
 $e=$all.Item($i)
 try {
  $name=$e.Current.Name
  $type=$e.Current.ControlType.ProgrammaticName
  $class=$e.Current.ClassName
  if([string]::IsNullOrWhiteSpace($name)){continue}
  # Explorer detail/list rows are normally DataItem/ListItem. Keep both,
  # but ignore obvious navigation/tree controls.
  if($type -in @('ControlType.DataItem','ControlType.ListItem')){
   $items.Add([pscustomobject]@{Name=$name;Type=$type;Class=$class})
  }
 } catch {}
}

# De-duplicate names while preserving order.
$seen=@{}
$unique=New-Object System.Collections.Generic.List[object]
foreach($item in $items){
 $key=$item.Type+'|'+$item.Name
 if(-not $seen.ContainsKey($key)){
  $seen[$key]=$true
  $unique.Add($item)
 }
}

Write-Host ('UI items found: '+$unique.Count)
$n=0
foreach($item in $unique){
 $n++
 Write-Host ('  ['+$n+'] ['+$item.Type.Replace('ControlType.','')+'] '+$item.Name)
 if($n -ge 120){break}
}
Write-Host ''
if($unique.Count -gt 0){
 Write-Host 'RESULT: Explorer items are readable through Windows UI Automation.'
 Write-Host 'NEXT: classify folders/Office files and navigate through UI Automation, not SendKeys.'
} else {
 Write-Host 'RESULT: no list items were exposed by UI Automation.'
 Write-Host 'NEXT: inspect Explorer accessibility tree; no automatic clicks were made.'
}
Write-Host 'SAFE STOP: read-only test completed.'
