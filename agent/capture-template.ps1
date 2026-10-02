param(
 [Parameter(Mandatory=$true)][string]$Name,
 [int]$Width=280,
 [int]$Height=90,
 [int]$Seconds=8
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Write-Host ''
Write-Host ('CloudSave Template Capture: '+$Name)
Write-Host 'Put the mouse at the CENTER of the target item. Do not click.'
for($i=$Seconds;$i -ge 1;$i--){Write-Host ('Capture in '+$i+' seconds...');Start-Sleep 1}
$p=[Windows.Forms.Cursor]::Position
$left=$p.X-[int]($Width/2);$top=$p.Y-[int]($Height/2)
$bmp=New-Object Drawing.Bitmap $Width,$Height
$g=[Drawing.Graphics]::FromImage($bmp);$g.CopyFromScreen($left,$top,0,0,$bmp.Size)
$dir='C:\cloudsave-batch\agent\templates';[IO.Directory]::CreateDirectory($dir)|Out-Null
$out=Join-Path $dir ($Name+'.png')
$bmp.Save($out,[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()
Write-Host ('OK: '+$out)
