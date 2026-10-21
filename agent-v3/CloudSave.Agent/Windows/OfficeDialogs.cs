using System.Windows.Automation;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Office prompts that block a document the agent opened, answered only with the choice that
/// changes nothing: Excel "update links?" -> 업데이트 안 함 / Don't Update; "file in use" ->
/// 읽기 전용 / Read Only. Only dialogs of an Office process, and only ones that were not already
/// on screen before the agent opened the file, are touched.
/// </summary>
internal static class OfficeDialogs
{
    public const string PowerPointClass = "PPTFrameClass";
    public const string ExcelClass = "XLMAIN";
    private static readonly string[] DialogClasses = { "#32770", "NUIDialog" };

    public static bool IsOfficeFrame(string className) =>
        className == PowerPointClass || className == ExcelClass;

    /// <summary>Process ids that currently own a PowerPoint or Excel frame window.</summary>
    public static HashSet<uint> OfficeProcessIds() =>
        NativeMethods.GetVisibleWindows()
            .Where(w => IsOfficeFrame(w.ClassName))
            .Select(w => NativeMethods.ProcessIdOf(w.Handle))
            .ToHashSet();

    /// <summary>Dialog windows of Office processes on screen right now (the "before" baseline).</summary>
    public static HashSet<IntPtr> CurrentDialogs()
    {
        var pids = OfficeProcessIds();
        return NativeMethods.GetVisibleWindows()
            .Where(w => DialogClasses.Contains(w.ClassName) && pids.Contains(NativeMethods.ProcessIdOf(w.Handle)))
            .Select(w => w.Handle)
            .ToHashSet();
    }

    /// <summary>
    /// Answer one open-time prompt with its non-modifying choice. <paramref name="pid"/> 0 means any
    /// Office process. Returns a description of what was clicked, or null.
    /// </summary>
    public static string? TryDismissOpenPrompts(uint pid, ISet<IntPtr>? ignore)
    {
        try
        {
            var pids = pid != 0 ? new HashSet<uint> { pid } : OfficeProcessIds();
            foreach (var w in NativeMethods.GetVisibleWindows())
            {
                if (!DialogClasses.Contains(w.ClassName)) continue;
                if (ignore is not null && ignore.Contains(w.Handle)) continue;
                if (!pids.Contains(NativeMethods.ProcessIdOf(w.Handle))) continue;

                var button = Uia.CollectByType(Uia.FromHandle(w.Handle), new[] { ControlType.Button },
                    h => !h.IsOffscreen && IsSafeOpenChoice(h.Name), CancellationToken.None, attempts: 1).FirstOrDefault();
                if (button is null) continue;

                Uia.Activate(button);
                Thread.Sleep(500);
                return $"\"{w.Title}\" -> \"{button.Name}\"";
            }
        }
        catch { /* best effort */ }
        return null;
    }

    /// <summary>
    /// Choose Don't Save on a save prompt belonging to the document being closed: searched in that
    /// document's own window and in dialogs of the same Office process only. Never matches Save / Save As.
    /// </summary>
    public static bool TryDismissSavePrompt(IntPtr documentWindow, uint pid)
    {
        try
        {
            var roots = new List<IntPtr>();
            if (NativeMethods.Exists(documentWindow)) roots.Add(documentWindow);
            roots.AddRange(NativeMethods.GetVisibleWindows()
                .Where(w => DialogClasses.Contains(w.ClassName) && NativeMethods.ProcessIdOf(w.Handle) == pid)
                .Select(w => w.Handle));

            foreach (var root in roots)
            {
                var hit = Uia.CollectByType(Uia.FromHandle(root), new[] { ControlType.Button },
                    h => !h.IsOffscreen && IsDontSave(h.Name), CancellationToken.None, attempts: 1).FirstOrDefault();
                if (hit is null) continue;
                Uia.Activate(hit);
                Thread.Sleep(400);
                return true;
            }
        }
        catch { /* best effort */ }
        return false;
    }

    // "저장 안 함" / "저장 안함" / "Don't Save" (incl. typographic apostrophe); never "저장", "Save", "Save As".
    private static bool IsDontSave(string name) =>
        name.Contains(Strings.DontSave, StringComparison.Ordinal)
        || name.Contains(Strings.DontSaveEn, StringComparison.OrdinalIgnoreCase)
        || name.Contains(Strings.DontSaveEnTypographic, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeOpenChoice(string name)
    {
        var n = name.Trim();
        return n.StartsWith(Strings.DontUpdate, StringComparison.Ordinal)
               || n.StartsWith(Strings.DontUpdateEn, StringComparison.OrdinalIgnoreCase)
               || n.StartsWith(Strings.ReadOnly, StringComparison.Ordinal)
               || n.StartsWith(Strings.ReadOnlyEn, StringComparison.OrdinalIgnoreCase);
    }
}
