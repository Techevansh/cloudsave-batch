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
    string ClassName,
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
    public static List<UiaHit> Collect(AutomationElement root, Func<UiaHit, bool> match, CancellationToken ct, int attempts = 4) =>
        CollectWhere(root, System.Windows.Automation.Condition.TrueCondition, match, ct, attempts);

    /// <summary>
    /// Like <see cref="Collect"/>, but only elements of the given control types are fetched
    /// (filtered by UIA itself), which is much cheaper on large Explorer / Office trees.
    /// </summary>
    public static List<UiaHit> CollectByType(
        AutomationElement root, ControlType[] types, Func<UiaHit, bool> match, CancellationToken ct, int attempts = 4)
    {
        System.Windows.Automation.Condition condition = types.Length == 1
            ? new PropertyCondition(AutomationElement.ControlTypeProperty, types[0])
            : new OrCondition(types.Select(t => (System.Windows.Automation.Condition)
                new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
        return CollectWhere(root, condition, match, ct, attempts);
    }

    private static List<UiaHit> CollectWhere(
        AutomationElement root, System.Windows.Automation.Condition condition, Func<UiaHit, bool> match,
        CancellationToken ct, int attempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hits = new List<UiaHit>();
                var all = root.FindAll(TreeScope.Descendants, condition);
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
                info.ClassName ?? string.Empty,
                info.IsOffscreen,
                info.BoundingRectangle);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The texts Explorer shows for one row (column cells such as type, size, date): every
    /// descendant's Name and, where exposed, its ValuePattern value. Best effort; never throws.
    /// </summary>
    public static List<string> CellTexts(AutomationElement row)
    {
        var texts = new List<string>();
        try
        {
            var all = row.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            for (var i = 0; i < all.Count; i++)
            {
                try
                {
                    var e = all[i];
                    var name = e.Current.Name;
                    if (!string.IsNullOrWhiteSpace(name)) texts.Add(name);
                    if (e.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                    {
                        var value = ((ValuePattern)vp).Current.Value;
                        if (!string.IsNullOrWhiteSpace(value)) texts.Add(value);
                    }
                }
                catch { /* one unreadable cell does not matter */ }
            }
        }
        catch { /* row vanished: no evidence */ }
        return texts;
    }

    public static IReadOnlyList<IntPtr> FindWindows(string className) =>
        NativeMethods.GetVisibleWindows()
            .Where(w => string.Equals(w.ClassName, className, StringComparison.Ordinal))
            .Select(w => w.Handle)
            .ToList();

    /// <summary>Scroll a list row into view (ScrollItemPattern). Returns true if the pattern was available.</summary>
    public static bool TryScrollIntoView(AutomationElement e)
    {
        try
        {
            if (e.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var p))
            {
                ((ScrollItemPattern)p).ScrollIntoView();
                return true;
            }
        }
        catch { /* best effort */ }
        return false;
    }

    /// <summary>
    /// Invoke -> Select -> single bounding-rect click (never a guessed coordinate). Returns the method used.
    /// When <paramref name="requireRoot"/> is given, a rect click is only sent if a hit-test confirms the
    /// point belongs to that top-level window; if anything else covers it, nothing is clicked and
    /// <see cref="ErrorCodes.ExplorerClickBlocked"/> is thrown.
    /// </summary>
    public static string Activate(UiaHit hit, bool doubleClick = false, IntPtr requireRoot = default)
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

            if (requireRoot != IntPtr.Zero)
            {
                var owner = NativeMethods.RootWindowAt(x, y);
                if (owner != requireRoot)
                    throw new AgentOperationException(ErrorCodes.ExplorerClickBlocked,
                        $"Click target \"{hit.Name}\" at {x},{y} is covered by another window " +
                        $"(\"{NativeMethods.TitleOf(owner)}\"); nothing was clicked.");
            }

            if (doubleClick) NativeMethods.DoubleClick(x, y);
            else NativeMethods.Click(x, y);
            return $"UIA_RECT@{x},{y}";
        }

        throw new AgentOperationException(ErrorCodes.Unexpected, "Element could not be safely activated (no pattern, not clickable).");
    }

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
