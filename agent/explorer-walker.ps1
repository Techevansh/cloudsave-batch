$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WalkerNative {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 public static IntPtr[] Explorers(){
  var list=new List<IntPtr>();
  EnumWindows(delegate(IntPtr h,IntPtr l){
   if(!IsWindowVisible(h)) return true;
   var c=new StringBuilder(128); GetClassName(h,c,c.Capacity);
   if(c.ToString()=="CabinetWClass") list.Add(h);
   return true;
  },IntPtr.Zero); return list.ToArray();
 }
 public static string Title(IntPtr h){var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
}
'@

function Read-Explorer([IntPtr]$h){
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
   if($seen.ContainsKey($key)){continue}; $seen[$key]=$true
   $ext=[IO.Path]::GetExtension($name).ToLowerInvariant()
   $kind=if($ext -in @('.pptx','.xlsx','.xls')){'OFFICE'}elseif([string]::IsNullOrWhiteSpace($ext)){'FOLDER-CANDIDATE'}else{'OTHER'}
   $rows.Add([pscustomobject]@{Name=$name;Kind=$kind;Element=$e})
  }catch{}
 }
 return $rows
}

Write-Host ''
Write-Host 'CloudSave Explorer Reader v0.4.1'
Write-Host 'Fix: choose the Explorer window that actually exposes the target item list.'
Write-Host 'READ-ONLY. No keyboard/mouse input. Nothing will be opened.'
Write-Host ''

$candidates=New-Object System.Collections.Generic.List[object]
foreach($h in [WalkerNative]::Explorers()){
 try{
  $title=[WalkerNative]::Title($h)
  $rows=@(Read-Explorer $h)
  $score=($rows.Count*2)+(@($rows|Where-Object Kind -eq 'OFFICE').Count*10)
  $candidates.Add([pscustomobject]@{Handle=$h;Title=$title;Rows=$rows;Score=$score})
  Write-Host ('Candidate: "'+$title+'" | items='+$rows.Count+' | office='+@($rows|Where-Object Kind -eq 'OFFICE').Count+' | score='+$score)
 }catch{}
}
if($candidates.Count -eq 0){throw 'No readable File Explorer window was found.'}
$chosen=$candidates|Sort-Object Score -Descending|Select-Object -First 1
$rows=@($chosen.Rows)
Write-Host ''
Write-Host ('Selected Explorer: '+$chosen.Title)
Write-Host ('Items: '+$rows.Count+' | Office: '+@($rows|Where-Object Kind -eq 'OFFICE').Count+' | Folder candidates: '+@($rows|Where-Object Kind -eq 'FOLDER-CANDIDATE').Count+' | Other: '+@($rows|Where-Object Kind -eq 'OTHER').Count)
Write-Host ''
$n=0
foreach($r in $rows){$n++;Write-Host ('  ['+$n+'] ['+$r.Kind+'] '+$r.Name)}
Write-Host ''
if($rows.Count -eq 0){
 Write-Host 'RESULT: Explorer window found, but no content rows were exposed. Keep the target folder visible and retry.'
}else{
 Write-Host 'RESULT: target Explorer content is readable again.'
 Write-Host 'NEXT: build guarded single-item UIA activation from the selected Explorer.'
}
Write-Host 'SAFE STOP: read-only test completed.'
