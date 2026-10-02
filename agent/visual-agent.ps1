$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class MouseInput {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int X,int Y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@
Write-Host ''
Write-Host 'CloudSave Visual Agent v0.10'
$templatePath='C:\cloudsave-batch\agent\templates\vtw-server-u.png'
if(-not(Test-Path $templatePath)){Write-Host 'ERROR: template missing. Capture it first.';exit 2}
$screen=[Windows.Forms.SystemInformation]::VirtualScreen
$shot=New-Object Drawing.Bitmap $screen.Width,$screen.Height
$g=[Drawing.Graphics]::FromImage($shot);$g.CopyFromScreen($screen.Left,$screen.Top,0,0,$shot.Size);$g.Dispose()
$tpl=[Drawing.Bitmap]::FromFile($templatePath)
$best=1e20;$bestX=0;$bestY=0
for($y=0;$y -le $shot.Height-$tpl.Height;$y+=6){
 for($x=0;$x -le $shot.Width-$tpl.Width;$x+=6){
  $score=0.0;$count=0
  for($ty=4;$ty -lt $tpl.Height;$ty+=12){
   for($tx=4;$tx -lt $tpl.Width;$tx+=12){
    $a=$shot.GetPixel($x+$tx,$y+$ty);$b=$tpl.GetPixel($tx,$ty)
    $score += [Math]::Abs($a.R-$b.R)+[Math]::Abs($a.G-$b.G)+[Math]::Abs($a.B-$b.B);$count++
   }
  }
  $score/=$count
  if($score -lt $best){$best=$score;$bestX=$x;$bestY=$y}
 }
}
$tpl.Dispose();$shot.Dispose()
Write-Host ('Best visual score: '+[Math]::Round($best,2))
if($best -gt 55){Write-Host 'ERROR: visual target not matched confidently.';exit 3}
$cx=$screen.Left+$bestX+120;$cy=$screen.Top+$bestY+40
Write-Host ('MATCH: VTW Server (U:) near '+$cx+','+$cy)
[MouseInput]::SetCursorPos($cx,$cy)|Out-Null
Start-Sleep -Milliseconds 500
1..2|ForEach-Object{[MouseInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[MouseInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 120}
Write-Host 'OK: double-click sent to matched visual target.'
