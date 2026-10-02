using System.Runtime.InteropServices;
using System.Text;

namespace CloudSave.Agent.Windows;

/// <summary>Win32 interop for window enumeration, foreground/activation, the single bounding-rect click, and the F12 hotkey.</summary>
internal static class NativeMethods
{
    public const int VK_F12 = 0x7B;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    // Waking Chromium/WebView2 accessibility: a UIA client asking a window for its
    // root object (WM_GETOBJECT + UiaRootObjectId) prompts the renderer to build its
    // accessibility tree. This is the non-intrusive way to make the Office.js task
    // pane expose its content to UI Automation (no clicks).
    private const uint WM_GETOBJECT = 0x003D;
    private static readonly IntPtr UiaRootObjectId = new(-25);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public readonly record struct WindowInfo(IntPtr Handle, string ClassName, string Title);

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

    public static bool BringToForeground(IntPtr h) => SetForegroundWindow(h);

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
