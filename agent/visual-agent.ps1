$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class FastVisual {
 public static int[] Match(Bitmap screen, Bitmap tpl) {
  Rectangle sr=new Rectangle(0,0,screen.Width,screen.Height), tr=new Rectangle(0,0,tpl.Width,tpl.Height);
  BitmapData sd=screen.LockBits(sr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  BitmapData td=tpl.LockBits(tr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  byte[] s=new byte[Math.Abs(sd.Stride)*sd.Height], t=new byte[Math.Abs(td.Stride)*td.Height];
  Marshal.Copy(sd.Scan0,s,0,s.Length); Marshal.Copy(td.Scan0,t,0,t.Length);
  screen.UnlockBits(sd); tpl.UnlockBits(td);
  double best=Double.MaxValue; int bx=0,by=0;
  for(int y=0;y<=screen.Height-tpl.Height;y+=4) for(int x=0;x<=screen.Width-tpl.Width;x+=4) {
   long sum=0; int count=0;
   for(int ty=4;ty<tpl.Height;ty+=12) for(int tx=4;tx<tpl.Width;tx+=12) {
    int si=y*sd.Stride+x*4+ty*sd.Stride+tx*4, ti=ty*td.Stride+tx*4;
    sum+=Math.Abs(s[si]-t[ti])+Math.Abs(s[si+1]-t[ti+1])+Math.Abs(s[si+2]-t[ti+2]); count++;
   }
   double score=(double)sum/count;
   if(score<best){best=score;bx=x;by=y;}
  }
  return new int[]{bx,by,(int)Math.Round(best)};
 }
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int X,int Y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@ -ReferencedAssemblies System.Drawing
Write-Host ''
Write-Host 'CloudSave Visual Agent v0.10.1'
$templatePath='C:\cloudsave-batch\agent\templates\vtw-server-u.png'
if(-not(Test-Path $templatePath)){Write-Host 'ERROR: template missing. Capture it first.';exit 2}
Write-Host '1/3 Capturing desktop...'
$screen=[Windows.Forms.SystemInformation]::VirtualScreen
$shot=New-Object Drawing.Bitmap $screen.Width,$screen.Height
$g=[Drawing.Graphics]::FromImage($shot);$g.CopyFromScreen($screen.Left,$screen.Top,0,0,$shot.Size);$g.Dispose()
$tpl=[Drawing.Bitmap]::FromFile($templatePath)
Write-Host '2/3 Finding VTW Server (U:) visually...'
$m=[FastVisual]::Match($shot,$tpl)
$tw=$tpl.Width;$th=$tpl.Height;$tpl.Dispose();$shot.Dispose()
Write-Host ('Visual score: '+$m[2])
if($m[2] -gt 55){Write-Host 'ERROR: target not matched confidently. No click was sent.';exit 3}
$cx=$screen.Left+$m[0]+[int]($tw/2);$cy=$screen.Top+$m[1]+[int]($th/2)
Write-Host ('MATCH: '+$cx+','+$cy)
Write-Host '3/3 Moving mouse and double-clicking...'
[FastVisual]::SetCursorPos($cx,$cy)|Out-Null
Start-Sleep -Milliseconds 400
1..2|ForEach-Object{[FastVisual]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[FastVisual]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 130}
Write-Host 'OK: double-click sent.'
