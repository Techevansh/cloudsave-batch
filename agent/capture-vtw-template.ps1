$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Write-Host ''
Write-Host 'CloudSave Visual Agent - Template Capture v0.10'
Write-Host 'Put Explorer on This PC and place the mouse over the CENTER of VTW Server (U:).'
Write-Host 'A 240x80 image around the mouse will be saved after 8 seconds.'
for($i=8;$i -ge 1;$i--){Write-Host ('Capture in '+$i+' seconds...');Start-Sleep 1}
$p=[System.Windows.Forms.Cursor]::Position
$w=240;$h=80;$left=$p.X-120;$top=$p.Y-40
$bmp=New-Object System.Drawing.Bitmap $w,$h
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($left,$top,0,0,$bmp.Size)
$dir='C:\cloudsave-batch\agent\templates'
[IO.Directory]::CreateDirectory($dir)|Out-Null
$out=Join-Path $dir 'vtw-server-u.png'
$bmp.Save($out,[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()
Write-Host ('OK: template saved: '+$out)
Write-Host 'Do not move or rename this file.'
