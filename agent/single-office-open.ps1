$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class SingleOpenNative {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 public static IntPtr[] Explorers(){var a=new List<IntPtr>();EnumWindows(delegate(IntPtr h,IntPtr l){if(!IsWindowVisible(h))return true;var c=new StringBuilder(128);GetClassName(h,c,c.Capacity);if(c.ToString()=="CabinetWClass")a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static string Title(IntPtr h){var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
}
'@

function Read-Explorer([IntPtr]$h){
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($h)
 $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $rows=@();$seen=@{}
 for($i=0;$i -lt $all.Count;$i++){
  $e=$all.Item($i)
  try{
   $name=$e.Current.Name;$type=$e.Current.ControlType.ProgrammaticName
   if([string]::IsNullOrWhiteSpace($name) -or $type -notin @('ControlType.DataItem','ControlType.ListItem')){continue}
   $key=$type+'|'+$name;if($seen[$key]){continue};$seen[$key]=$true
   $ext=[IO.Path]::GetExtension($name).ToLowerInvariant()
   $kind=if($ext -in @('.pptx','.xlsx','.xls')){'OFFICE'}elseif([string]::IsNullOrWhiteSpace($ext)){'FOLDER'}else{'OTHER'}
   $rows += [pscustomobject]@{Name=$name;Kind=$kind;Element=$e}
  }catch{}
 }
 return $rows
}

Write-Host ''
Write-Host 'CloudSave Single Office Open Test v0.5'
Write-Host 'GUARDRAIL: exactly ONE Office file may be opened. No CloudSave action. No keyboard/mouse injection.'
Write-Host ''

$c=@()
foreach($h in [SingleOpenNative]::Explorers()){
 try{$r=@(Read-Explorer $h);$c += [pscustomobject]@{H=$h;Title=[SingleOpenNative]::Title($h);Rows=$r;Score=($r.Count*2)+(@($r|? Kind -eq 'OFFICE').Count*10)}}catch{}
}
if(!$c){throw 'No readable File Explorer found.'}
$x=$c|sort Score -Descending|select -First 1
$office=@($x.Rows|? Kind -eq 'OFFICE')
Write-Host ('Explorer: '+$x.Title)
Write-Host ('Office files: '+$office.Count)
if(!$office){throw 'No PPTX/XLSX/XLS is visible in the selected Explorer.'}

$preferred=$office|? Name -eq 'Summary.pptx'|select -First 1
$target=if($preferred){$preferred}else{$office|select -First 1}
Write-Host ('TARGET (one file only): '+$target.Name)

$el=$target.Element
$pattern=$null
$method=''
if($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){
 $method='InvokePattern'
 Write-Host 'Activation method: InvokePattern'
 $pattern.Invoke()
}else{
 $pattern=$null
 if($el.TryGetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern,[ref]$pattern)){
  $method='LegacyIAccessible.DefaultAction'
  Write-Host 'Activation method: LegacyIAccessible.DefaultAction'
  $pattern.DoDefaultAction()
 }else{
  throw 'Target row has no safe UI Automation activation pattern. Nothing was opened.'
 }
}

Write-Host 'Activation request sent. Waiting up to 15 seconds for PowerPoint/Excel window...'
$deadline=(Get-Date).AddSeconds(15);$opened=$false
while((Get-Date)-lt $deadline){
 Start-Sleep -Milliseconds 500
 $p=Get-Process POWERPNT,EXCEL -ErrorAction SilentlyContinue
 if($p){$opened=$true;break}
}
if($opened){
 Write-Host 'SUCCESS: an Office application is running after the single-item activation.'
 Write-Host 'STOP: CloudSave was NOT clicked. Leave the Office file open for the next test.'
}else{
 Write-Host 'WARNING: activation was sent, but PowerPoint/Excel was not detected within 15 seconds.'
 Write-Host 'No second item will be attempted.'
}
