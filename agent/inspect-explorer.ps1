$ErrorActionPreference='Stop'
Write-Host ''
Write-Host 'CloudSave Explorer UI Inspector v0.7'
Write-Host 'Open File Explorer at This PC before running this inspector.'
Write-Host ''
$e=Get-Process explorer -ErrorAction SilentlyContinue|Where-Object{$_.MainWindowHandle -ne 0}|Select-Object -First 1
if($null -eq $e){Write-Host 'ERROR: Explorer window not found.';exit 2}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$r=[Windows.Automation.AutomationElement]::FromHandle($e.MainWindowHandle)
$all=$r.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
$out=@()
for($n=0;$n -lt $all.Count;$n++){
 try{
  $el=$all.Item($n);$name=$el.Current.Name;$type=$el.Current.ControlType.ProgrammaticName
  $aid=$el.Current.AutomationId;$cls=$el.Current.ClassName;$rect=$el.Current.BoundingRectangle
  if(-not [string]::IsNullOrWhiteSpace($name)){
   $out += [pscustomobject]@{Name=$name;Type=$type;AutomationId=$aid;Class=$cls;X=[int]$rect.X;Y=[int]$rect.Y;W=[int]$rect.Width;H=[int]$rect.Height}
  }
 }catch{}
}
$path=Join-Path $env:TEMP 'cloudsave-explorer-ui.txt'
$out|Format-Table -AutoSize|Out-String -Width 300|Set-Content -Encoding UTF8 $path
Write-Host ('Captured '+$out.Count+' named UI elements.')
Write-Host ('Saved: '+$path)
Write-Host ''
Get-Content $path
