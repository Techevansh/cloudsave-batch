$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CloudSaveVision {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public static IntPtr FindExplorer() {
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h,IntPtr l){
   if(!IsWindowVisible(h)) return true;
   var c=new StringBuilder(128);GetClassName(h,c,c.Capacity);
   if(c.ToString()=="CabinetWClass"){found=h;return false;} return true;
  },IntPtr.Zero); return found;
 }
 public static string Title(IntPtr h){var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
 public static int[] Match(Bitmap screen,Bitmap tpl) {
  var sr=new Rectangle(0,0,screen.Width,screen.Height);var tr=new Rectangle(0,0,tpl.Width,tpl.Height);
  var sd=screen.LockBits(sr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  var td=tpl.LockBits(tr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  int ss=Math.Abs(sd.Stride),ts=Math.Abs(td.Stride);byte[] s=new byte[ss*sd.Height],t=new byte[ts*td.Height];
  Marshal.Copy(sd.Scan0,s,0,s.Length);Marshal.Copy(td.Scan0,t,0,t.Length);screen.UnlockBits(sd);tpl.UnlockBits(td);
  double best=Double.MaxValue;int bx=0,by=0;
  for(int y=0;y<=screen.Height-tpl.Height;y+=3)for(int x=0;x<=screen.Width-tpl.Width;x+=3){
   long sum=0;int count=0;
   for(int ty=3;ty<tpl.Height;ty+=10)for(int tx=3;tx<tpl.Width;tx+=10){
    int si=(y+ty)*ss+(x+tx)*4,ti=ty*ts+tx*4;
    sum+=Math.Abs(s[si]-t[ti])+Math.Abs(s[si+1]-t[ti+1])+Math.Abs(s[si+2]-t[ti+2]);count++;
   }
   double score=(double)sum/count;if(score<best){best=score;bx=x;by=y;}
  } return new int[]{bx,by,(int)Math.Round(best)};
 }
}
'@ -ReferencedAssemblies System.Drawing

function Get-Explorer {
 $h=[CloudSaveVision]::FindExplorer()
 if($h -eq [IntPtr]::Zero){return $null}
 return $h
}
function Wait-Explorer([int]$Seconds=8){
 for($i=0;$i -lt ($Seconds*4);$i++){ $h=Get-Explorer; if($null -ne $h){return $h}; Start-Sleep -Milliseconds 250 }
 return $null
}
function Open-ThisPC {
 Write-Host '1/5 Starting File Explorer at This PC...'
 Start-Process explorer.exe 'shell:MyComputerFolder'
 $h=Wait-Explorer 10
 if($null -eq $h){throw 'Explorer did not appear.'}
 [CloudSaveVision]::ShowWindow($h,9)|Out-Null
 [CloudSaveVision]::SetForegroundWindow($h)|Out-Null
 Start-Sleep -Milliseconds 1200
 return $h
}
function Match-And-Open($h,[string]$Template,[string]$Label,[int]$Threshold=48){
 $r=New-Object CloudSaveVision+RECT
 [CloudSaveVision]::GetWindowRect($h,[ref]$r)|Out-Null
 $w=$r.Right-$r.Left;$hh=$r.Bottom-$r.Top
 if($w -lt 500 -or $hh -lt 350){throw 'Explorer window is too small.'}
 $shot=New-Object Drawing.Bitmap $w,$hh
 $g=[Drawing.Graphics]::FromImage($shot);$g.CopyFromScreen($r.Left,$r.Top,0,0,$shot.Size);$g.Dispose()
 $tpl=[Drawing.Bitmap]::FromFile($Template)
 $m=[CloudSaveVision]::Match($shot,$tpl);$tw=$tpl.Width;$th=$tpl.Height;$tpl.Dispose();$shot.Dispose()
 Write-Host ('   '+$Label+' score: '+$m[2])
 if($m[2] -gt $Threshold){throw ($Label+' was not matched confidently. Nothing was clicked.')}
 $x=$r.Left+$m[0]+[int]($tw/2);$y=$r.Top+$m[1]+[int]($th/2)
 Write-Host ('   MATCH '+$Label+': '+$x+','+$y)
 [CloudSaveVision]::SetForegroundWindow($h)|Out-Null;Start-Sleep -Milliseconds 200
 [CloudSaveVision]::SetCursorPos($x,$y)|Out-Null;Start-Sleep -Milliseconds 300
 1..2|%{[CloudSaveVision]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[CloudSaveVision]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 140}
}

Write-Host ''
Write-Host 'CloudSave Visual Navigator v0.12'
Write-Host 'No Explorer preparation is required.'
$vtw='C:\cloudsave-batch\agent\templates\vtw-server-u.png'
if(-not(Test-Path $vtw)){throw 'VTW template is missing.'}

# Always create our own Explorer window so the user does not have to prepare one.
$h=Open-ThisPC
Write-Host ('2/5 Explorer ready. Title: '+[CloudSaveVision]::Title($h))
Write-Host '3/5 Looking for VTW Server (U:) only inside this Explorer window...'
Match-And-Open $h $vtw 'VTW Server (U:)'
Write-Host '4/5 Waiting for the U: view to load...'
Start-Sleep -Seconds 2
$title=[CloudSaveVision]::Title($h)
Write-Host ('   Explorer title now: '+$title)
if($title -notmatch 'VTW|U:'){
 Write-Host 'WARNING: U: could not be verified from the Explorer title.'
 Write-Host 'No further automatic clicks will be made.'
 exit 6
}
Write-Host '5/5 VERIFIED: Explorer changed after opening VTW Server (U:).'
Write-Host 'SAFE STOP: next folder navigation is not enabled until its template is captured.'
