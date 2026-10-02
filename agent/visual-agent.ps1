$ErrorActionPreference='Stop'
Write-Host ''
Write-Host 'CloudSave Visual Agent v0.9'
Write-Host 'Mode: image recognition, no filesystem access'
Write-Host ''

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MouseInput {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int X,int Y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@

$screen=[System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp=New-Object System.Drawing.Bitmap $screen.Width,$screen.Height
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Left,$screen.Top,0,0,$bmp.Size)
$out=Join-Path $env:TEMP 'cloudsave-screen.png'
$bmp.Save($out,[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()

Write-Host ('OK: virtual desktop captured: '+$out)
Write-Host ('Bounds: '+$screen.Left+','+$screen.Top+' '+$screen.Width+'x'+$screen.Height)
Write-Host 'Next: visual detector will locate the fixed VTW Server (U:) target from this capture.'
