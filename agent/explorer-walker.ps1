$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
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

function Get-Pattern([System.Windows.Automation.AutomationElement]$Element,[System.Windows.Automation.AutomationPattern]$Pattern){
 $obj=$null
 if($Element.TryGetCurrentPattern($Pattern,[ref]$obj)){return $obj}
 return $null
}

Write-Host ''
Write-Host 'CloudSave Explorer Reader v0.4'
Write-Host 'READ-ONLY classification + UIA capability probe'
Write-Host 'No keyboard injection. No mouse clicks. Nothing will be opened.'
Write-Host ''

$h=[WalkerNative]::Explorer()
if($h -eq [IntPtr]::Zero){throw 'No File Explorer window is open. Open the target folder first.'}
$title=[WalkerNative]::Title($h)
Write-Host ('Explorer detected: '+$title)

$root=[System.Windows.Automation.AutomationElement]::FromHandle($h)
$all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)

$rows=New-Object System.Collections.Generic.List[object]
$seen=@{}
for($i=0;$i -lt $all.Count;$i++){
 $e=$all.Item($i)
 try{
  $name=$e.Current.Name
  $type=$e.Current.ControlType.ProgrammaticName
  if([string]::IsNullOrWhiteSpace($name)){continue}
  if($type -notin @('ControlType.DataItem','ControlType.ListItem')){continue}
  $key=$type+'|'+$name
  if($seen.ContainsKey($key)){continue}
  $seen[$key]=$true

  $ext=[System.IO.Path]::GetExtension($name).ToLowerInvariant()
  $kind='OTHER'
  if($ext -in @('.pptx','.xlsx','.xls')){$kind='OFFICE'}
  elseif([string]::IsNullOrWhiteSpace($ext)){$kind='FOLDER-CANDIDATE'}

  $invoke=Get-Pattern $e ([System.Windows.Automation.InvokePattern]::Pattern)
  $selection=Get-Pattern $e ([System.Windows.Automation.SelectionItemPattern]::Pattern)
  $legacy=Get-Pattern $e ([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)

  $rows.Add([pscustomobject]@{
    Index=$rows.Count+1; Name=$name; Kind=$kind;
    Invoke=($null -ne $invoke); Select=($null -ne $selection); Legacy=($null -ne $legacy)
  })
 }catch{}
}

$office=@($rows|Where-Object Kind -eq 'OFFICE')
$folders=@($rows|Where-Object Kind -eq 'FOLDER-CANDIDATE')
$other=@($rows|Where-Object Kind -eq 'OTHER')

Write-Host ('Items: '+$rows.Count+' | Office: '+$office.Count+' | Folder candidates: '+$folders.Count+' | Other: '+$other.Count)
Write-Host ''
foreach($r in $rows){
 $caps=@()
 if($r.Invoke){$caps+='Invoke'}
 if($r.Select){$caps+='Select'}
 if($r.Legacy){$caps+='Legacy'}
 $capText=if($caps.Count){$caps -join ','}else{'read-only'}
 Write-Host ('  ['+$r.Index+'] ['+$r.Kind+'] ['+$capText+'] '+$r.Name)
}

Write-Host ''
if($office.Count){
 Write-Host 'Office files detected:'
 foreach($r in $office){Write-Host ('  - '+$r.Name)}
}
Write-Host ''
$actionable=@($rows|Where-Object {$_.Invoke -or $_.Select -or $_.Legacy})
Write-Host ('UIA actionable items: '+$actionable.Count+' / '+$rows.Count)
if($actionable.Count -gt 0){
 Write-Host 'RESULT: classification works and Explorer exposes automation patterns.'
 Write-Host 'NEXT: add a guarded single-item open test using UI Automation only.'
}else{
 Write-Host 'RESULT: classification works, but rows expose no direct UIA action pattern.'
 Write-Host 'NEXT: inspect parent/child automation elements before attempting navigation.'
}
Write-Host 'SAFE STOP: read-only test completed; nothing was opened.'
