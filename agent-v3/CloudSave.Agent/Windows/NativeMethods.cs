using System.Runtime.InteropServices;
using System.Text;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Win32 interop for window enumeration, foreground/activation, the single bounding-rect click
/// (with a hit-test guard), DPI awareness, and the F12 hotkey.
/// </summary>
internal static class NativeMethods
{
    public const int VK_F12 = 0x7B;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const int SW_RESTORE = 9;
    private const uint GA_ROOT = 2;

    // Waking Chromium/WebView2 accessibility: a UIA client asking a window for its
    // root object (WM_GETOBJECT + UiaRootObjectId) prompts the renderer to build its
    // accessibility tree. This is the non-intrusive way to make the Office.js task
    // pane expose its content to UI Automation (no clicks).
    private const uint WM_GETOBJECT = 0x003D;
    private static readonly IntPtr UiaRootObjectId = new(-25);

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    public readonly record struct WindowInfo(IntPtr Handle, string ClassName, string Title);

    /// <summary>
    /// Make this process per-monitor DPI aware, so UI Automation rectangles, window rectangles,
    /// hit-tests and SetCursorPos all use the same physical pixels. Without this, on a display
    /// scaled above 100% a click aimed at a UIA rectangle lands in the wrong place.
    /// Must run before any window or UIA call.
    /// </summary>
    public static void EnableDpiAwareness()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return;
        }
        catch { /* older Windows: fall through */ }
        try { SetProcessDPIAware(); } catch { /* best effort */ }
    }

    /// <summary>Visible top-level windows in Z order (topmost first, as EnumWindows returns them).</summary>
    public static IReadOnlyList<WindowInfo> GetVisibleWindows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h))
                list.Add(new WindowInfo(h, ClassOf(h), TitleOf(h)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string TitleOf(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool Exists(IntPtr h) => h != IntPtr.Zero && IsWindow(h);

    public static bool IsVisible(IntPtr h) => IsWindowVisible(h);

    public static uint ProcessIdOf(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid;
    }

    public static RECT RectOf(IntPtr h) => GetWindowRect(h, out var r) ? r : default;

    /// <summary>Plain SetForegroundWindow (used while polling Office; never steals focus from a sign-in window).</summary>
    public static bool BringToForeground(IntPtr h) => SetForegroundWindow(h);

    /// <summary>
    /// Reliably bring a window to the front before clicking into it: restore if minimised, and
    /// briefly attach to the current foreground thread so Windows' foreground lock allows the switch.
    /// </summary>
    public static bool ForceForeground(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);

        var fg = GetForegroundWindow();
        if (fg == h) return true;

        var current = GetCurrentThreadId();
        var fgThread = fg == IntPtr.Zero ? 0u : GetWindowThreadProcessId(fg, out _);
        var attached = fgThread != 0 && fgThread != current && AttachThreadInput(current, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(current, fgThread, false);
        }
        return GetForegroundWindow() == h;
    }

    /// <summary>The top-level window that would receive a click at this screen point.</summary>
    public static IntPtr RootWindowAt(int x, int y)
    {
        var child = WindowFromPoint(new POINT { X = x, Y = y });
        return child == IntPtr.Zero ? IntPtr.Zero : GetAncestor(child, GA_ROOT);
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void DoubleClick(int x, int y)
    {
        Click(x, y);
        Thread.Sleep(130);
        Click(x, y);
    }

    public static bool IsF12Down() => (GetAsyncKeyState(VK_F12) & 0x8000) != 0;

    /// <summary>All descendant child windows of a parent (EnumChildWindows recurses into grandchildren too).</summary>
    public static IReadOnlyList<WindowInfo> GetChildWindows(IntPtr parent)
    {
        var list = new List<WindowInfo>();
        EnumChildWindows(parent, (h, _) =>
        {
            list.Add(new WindowInfo(h, ClassOf(h), TitleOf(h)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Prompt a window to build its UIA/accessibility tree (wakes Chromium/WebView2 content).</summary>
    public static void WakeAccessibility(IntPtr h)
    {
        try { SendMessageW(h, WM_GETOBJECT, IntPtr.Zero, UiaRootObjectId); } catch { /* best effort */ }
    }
}
