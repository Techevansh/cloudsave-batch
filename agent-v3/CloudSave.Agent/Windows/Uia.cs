using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>A safely-captured snapshot of a UI Automation element (reading Current.* can throw, so we cache once).</summary>
internal sealed record UiaHit(
    AutomationElement Element,
    string Name,
    string AutomationId,
    string ControlType,
    bool IsOffscreen,
    Rect Rect);

internal static class Uia
{
    public static AutomationElement Root => AutomationElement.RootElement;

    public static AutomationElement FromHandle(IntPtr h) => AutomationElement.FromHandle(h);

    /// <summary>True for UIA conditions that are worth a bounded retry (tree changed mid-scan, element vanished, COM hiccup).</summary>
    public static bool IsTransient(Exception ex) =>
        ex is ElementNotAvailableException
        || ex is COMException
        || (ex is InvalidOperationException && ex.Message.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase));

    /// <summary>Collect descendant elements matching a predicate. The scan itself is retried on transient UIA errors.</summary>
    public static List<UiaHit> Collect(AutomationElement root, Func<UiaHit, bool> match, CancellationToken ct, int attempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hits = new List<UiaHit>();
                var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                for (var i = 0; i < all.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var hit = Capture(all[i]);
                    if (hit is not null && match(hit)) hits.Add(hit);
                }
                return hits;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransient(ex) && attempt < attempts)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    /// <summary>Capture an element's current properties, swallowing transient read failures (returns null then).</summary>
    public static UiaHit? Capture(AutomationElement e)
    {
        try
        {
            var info = e.Current;
            return new UiaHit(
                e,
                info.Name ?? string.Empty,
                info.AutomationId ?? string.Empty,
                info.ControlType?.ProgrammaticName ?? string.Empty,
                info.IsOffscreen,
                info.BoundingRectangle);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<IntPtr> FindWindows(string className) =>
        NativeMethods.GetVisibleWindows()
            .Where(w => string.Equals(w.ClassName, className, StringComparison.Ordinal))
            .Select(w => w.Handle)
            .ToList();

    /// <summary>Invoke -> Select -> single bounding-rect click (never a guessed coordinate). Returns the method used.</summary>
    public static string Activate(UiaHit hit, bool doubleClick = false)
    {
        var e = hit.Element;

        if (!doubleClick && e.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
        {
            ((InvokePattern)inv).Invoke();
            return "InvokePattern";
        }

        if (!doubleClick && e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel))
        {
            ((SelectionItemPattern)sel).Select();
            return "SelectionItemPattern";
        }

        if (hit.Rect is { Width: > 0, Height: > 0 } r && !hit.IsOffscreen)
        {
            var x = (int)(r.X + r.Width / 2);
            var y = (int)(r.Y + r.Height / 2);
            if (doubleClick) NativeMethods.DoubleClick(x, y);
            else NativeMethods.Click(x, y);
            return $"UIA_RECT@{x},{y}";
        }

        throw new AgentOperationException(ErrorCodes.Unexpected, "Element could not be safely activated (no pattern, not clickable).");
    }

    public static void TrySetForeground(IntPtr h) => NativeMethods.BringToForeground(h);

    public static void TryMaximize(AutomationElement window)
    {
        try
        {
            if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var p))
                ((WindowPattern)p).SetWindowVisualState(WindowVisualState.Maximized);
        }
        catch { /* best effort */ }
    }
}
