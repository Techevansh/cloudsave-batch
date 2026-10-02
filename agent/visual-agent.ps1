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
  int sw=screen.Width,sh=screen.Height,tw=tpl.Width,th=tpl.Height;
  if(tw>sw||th>sh) return new int[]{0,0,999};
  var sr=new Rectangle(0,0,sw,sh);var tr=new Rectangle(0,0,tw,th);
  var sd=screen.LockBits(sr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  var td=tpl.LockBits(tr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  int ss=Math.Abs(sd.Stride),ts=Math.Abs(td.Stride);byte[] s=new byte[ss*sd.Height],t=new byte[ts*td.Height];
  Marshal.Copy(sd.Scan0,s,0,s.Length);Marshal.Copy(td.Scan0,t,0,t.Length);screen.UnlockBits(sd);tpl.UnlockBits(td);
  double best=Double.MaxValue;int bx=0,by=0;
  for(int y=0;y<=sh-th;y+=2)for(int x=0;x<=sw-tw;x+=2){
   long sum=0;int count=0;
   for(int ty=2;ty<th;ty+=7)for(int tx=2;tx<tw;tx+=7){
    int si=(y+ty)*ss+(x+tx)*4,ti=ty*ts+tx*4;
    sum+=Math.Abs(s[si]-t[ti])+Math.Abs(s[si+1]-t[ti+1])+Math.Abs(s[si+2]-t[ti+2]);count++;
   }
   double score=(double)sum/Math.Max(1,count);if(score<best){best=score;bx=x;by=y;}
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
 Start-Sleep -Milliseconds 1400
 return $h
}
function Capture-ExplorerContent($h) {
 $r=New-Object CloudSaveVision+RECT
 [CloudSaveVision]::GetWindowRect($h,[ref]$r)|Out-Null
 $w=$r.Right-$r.Left;$hh=$r.Bottom-$r.Top
 if($w -lt 700 -or $hh -lt 450){throw 'Explorer window is too small.'}
 # Critical: search ONLY the right main pane. Never include Home/left navigation.
 $left=[Math]::Max(430,[int]($w*0.31))
 $top=[Math]::Max(105,[int]($hh*0.11))
 $cw=$w-$left-20;$ch=$hh-$top-20
 $bmp=New-Object Drawing.Bitmap $cw,$ch
 $g=[Drawing.Graphics]::FromImage($bmp)
 $g.CopyFromScreen($r.Left+$left,$r.Top+$top,0,0,$bmp.Size)
 $g.Dispose()
 return @{ Bitmap=$bmp; Rect=$r; Left=$left; Top=$top }
}
function Find-VtwTile($h,[string]$Template) {
 $cap=Capture-ExplorerContent $h
 $tpl=[Drawing.Bitmap]::FromFile($Template)
 try {
   $m=[CloudSaveVision]::Match($cap.Bitmap,$tpl)
   $x=$cap.Rect.Left+$cap.Left+$m[0]+[int]($tpl.Width/2)
   $y=$cap.Rect.Top+$cap.Top+$m[1]+[int]($tpl.Height/2)
   return @{Score=$m[2];X=$x;Y=$y;Width=$tpl.Width;Height=$tpl.Height}
 } finally {$tpl.Dispose();$cap.Bitmap.Dispose()}
}
function DoubleClick([int]$x,[int]$y) {
 [CloudSaveVision]::SetCursorPos($x,$y)|Out-Null
 Start-Sleep -Milliseconds 450
 1..2|%{[CloudSaveVision]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[CloudSaveVision]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150}
}

Write-Host ''
Write-Host 'CloudSave Visual Navigator v0.15'
Write-Host 'Mode: main-pane template detection (no guessed drive coordinates)'
$vtw='C:\cloudsave-batch\agent\templates\vtw-server-u.png'
if(-not(Test-Path $vtw)){throw 'VTW template is missing.'}

$h=Open-ThisPC
Write-Host ('2/5 Explorer ready. Title: '+[CloudSaveVision]::Title($h))
Write-Host '3/5 Detecting the saved VTW Server (U:) tile inside MAIN PANE only...'
$hit=Find-VtwTile $h $vtw
Write-Host ('   VTW template score: '+$hit.Score)
Write-Host ('   Detected center: '+$hit.X+','+$hit.Y)
# Existing template was captured from this exact machine. Keep a conservative cutoff.
if($hit.Score -gt 70){
 Write-Host 'STOP: VTW tile was not matched confidently. Nothing was clicked.'
 exit 5
}
Write-Host '4/5 MATCH accepted. Moving pointer to detected VTW tile...'
[CloudSaveVision]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 250
DoubleClick $hit.X $hit.Y
Write-Host '   Double-click sent to detected template center.'
Write-Host '5/5 Verifying Explorer title...'
Start-Sleep -Seconds 2
$title=[CloudSaveVision]::Title($h)
Write-Host ('   Explorer title now: '+$title)
if($title -match 'VTW.*U|VTW 서버'){
 Write-Host 'VERIFIED: VTW Server (U:) opened.'
 exit 0
}
Write-Host 'STOP: Explorer did not confirm VTW Server (U:). No more clicks will be made.'
exit 6
