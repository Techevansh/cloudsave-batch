$ErrorActionPreference='Stop'
Write-Host ''
Write-Host 'CloudSave Explorer Accessibility Inspector v0.8'
Write-Host 'Move the mouse over the VTW Server (U:) text or icon within 10 seconds. Do not click.'
Write-Host ''

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Accessibility;

public static class AccProbe {
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(
        POINT pt,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible acc,
        [MarshalAs(UnmanagedType.Struct)] out object child);

    public static string Probe() {
        POINT p;
        GetCursorPos(out p);
        IAccessible acc;
        object child;
        int hr = AccessibleObjectFromPoint(p, out acc, out child);
        if (hr != 0 || acc == null) return "MSAA ERROR hr=" + hr;

        object childId = child ?? 0;
        string name = "";
        string role = "";
        string value = "";
        try { name = acc.get_accName(childId) ?? ""; } catch {}
        try { role = Convert.ToString(acc.get_accRole(childId)); } catch {}
        try { value = acc.get_accValue(childId) ?? ""; } catch {}

        return "Mouse: " + p.X + "," + p.Y +
               Environment.NewLine + "MSAA Name: " + name +
               Environment.NewLine + "MSAA Role: " + role +
               Environment.NewLine + "MSAA Value: " + value +
               Environment.NewLine + "ChildId: " + Convert.ToString(childId);
    }
}
'@ -ReferencedAssemblies Accessibility

for($i=10;$i -ge 1;$i--){
  Write-Host ('Capture in '+$i+' seconds...')
  Start-Sleep -Seconds 1
}

Write-Host ''
Write-Host ([AccProbe]::Probe())
Write-Host ''
Write-Host 'Inspector finished.'
