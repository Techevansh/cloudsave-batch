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
 public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd,out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd,int cmd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);

 public static IntPtr FindExplorerThisPC() {
   IntPtr found=IntPtr.Zero;
   EnumWindows(delegate(IntPtr h,IntPtr l) {
     if(!IsWindowVisible(h)) return true;
     var cls=new StringBuilder(256); GetClassName(h,cls,cls.Capacity);
     if(cls.ToString()!="CabinetWClass") return true;
     var title=new StringBuilder(512); GetWindowText(h,title,title.Capacity);
     string t=title.ToString();
     if(t.Contains("내 PC") || t.IndexOf("This PC",StringComparison.OrdinalIgnoreCase)>=0) { found=h; return false; }
     return true;
   },IntPtr.Zero);
   return found;
 }

 public static int[] Match(Bitmap screen, Bitmap tpl) {
   Rectangle sr=new Rectangle(0,0,screen.Width,screen.Height), tr=new Rectangle(0,0,tpl.Width,tpl.Height);
   BitmapData sd=screen.LockBits(sr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   BitmapData td=tpl.LockBits(tr,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   int ss=Math.Abs(sd.Stride), ts=Math.Abs(td.Stride);
   byte[] s=new byte[ss*sd.Height], t=new byte[ts*td.Height];
   Marshal.Copy(sd.Scan0,s,0,s.Length); Marshal.Copy(td.Scan0,t,0,t.Length);
   screen.UnlockBits(sd); tpl.UnlockBits(td);
   double best=Double.MaxValue; int bx=0,by=0;
   for(int y=0;y<=screen.Height-tpl.Height;y+=3) for(int x=0;x<=screen.Width-tpl.Width;x+=3) {
     long sum=0; int count=0;
     for(int ty=3;ty<tpl.Height;ty+=10) for(int tx=3;tx<tpl.Width;tx+=10) {
       int si=(y+ty)*ss+(x+tx)*4, ti=ty*ts+tx*4;
       sum+=Math.Abs(s[si]-t[ti])+Math.Abs(s[si+1]-t[ti+1])+Math.Abs(s[si+2]-t[ti+2]); count++;
     }
     double score=(double)sum/count;
     if(score<best){best=score;bx=x;by=y;}
   }
   return new int[]{bx,by,(int)Math.Round(best)};
 }
}
'@ -ReferencedAssemblies System.Drawing

Write-Host ''
Write-Host 'CloudSave Visual Agent v0.11'
Write-Host 'Mode: Explorer-window anchored visual navigation'

$templatePath='C:\cloudsave-batch\agent\templates\vtw-server-u.png'
if(-not(Test-Path $templatePath)){Write-Host 'ERROR: template missing. Run capture-vtw-template.ps1 first.';exit 2}

Write-Host '1/4 Finding the visible This PC Explorer window...'
$hwnd=[CloudSaveVision]::FindExplorerThisPC()
if($hwnd -eq [IntPtr]::Zero){Write-Host 'ERROR: This PC Explorer window was not found. Open File Explorer > This PC first.';exit 3}

[CloudSaveVision]::ShowWindow($hwnd,9)|Out-Null
[CloudSaveVision]::SetForegroundWindow($hwnd)|Out-Null
Start-Sleep -Milliseconds 700
$r=New-Object CloudSaveVision+RECT
[CloudSaveVision]::GetWindowRect($hwnd,[ref]$r)|Out-Null
$ww=$r.Right-$r.Left;$wh=$r.Bottom-$r.Top
if($ww -lt 500 -or $wh -lt 400){Write-Host 'ERROR: Explorer window bounds are invalid.';exit 4}
Write-Host ('Explorer bounds: '+$r.Left+','+$r.Top+' '+$ww+'x'+$wh)

Write-Host '2/4 Capturing only that Explorer window...'
$shot=New-Object Drawing.Bitmap $ww,$wh
$g=[Drawing.Graphics]::FromImage($shot)
$g.CopyFromScreen($r.Left,$r.Top,0,0,$shot.Size)
$g.Dispose()
$tpl=[Drawing.Bitmap]::FromFile($templatePath)

Write-Host '3/4 Finding VTW Server (U:) inside Explorer...'
$m=[CloudSaveVision]::Match($shot,$tpl)
$tw=$tpl.Width;$th=$tpl.Height
$tpl.Dispose();$shot.Dispose()
Write-Host ('Visual score: '+$m[2])
if($m[2] -gt 55){Write-Host 'ERROR: target not matched confidently. No click was sent.';exit 5}

$cx=$r.Left+$m[0]+[int]($tw/2)
$cy=$r.Top+$m[1]+[int]($th/2)
Write-Host ('MATCH: '+$cx+','+$cy)

Write-Host '4/4 Double-clicking VTW Server (U:)...'
[CloudSaveVision]::SetForegroundWindow($hwnd)|Out-Null
Start-Sleep -Milliseconds 250
[CloudSaveVision]::SetCursorPos($cx,$cy)|Out-Null
Start-Sleep -Milliseconds 350
1..2|ForEach-Object{
 [CloudSaveVision]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 [CloudSaveVision]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 140
}
Write-Host 'OK: double-click sent.'
