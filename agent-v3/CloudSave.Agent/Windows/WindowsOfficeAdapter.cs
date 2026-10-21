using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Finds the Office window that THIS open produced and closes it without modifying the source.
/// PowerPoint = PPTFrameClass, Excel = XLMAIN.
///
/// Safety rule: windows that already existed before the agent opened the file are never driven
/// or closed (a user's own document, possibly with unsaved edits, is never touched). The one
/// exception is a blank workbook/presentation Office replaced with this file (its title changed
/// to this file's name), which holds no user data.
/// </summary>
internal sealed class WindowsOfficeAdapter : IOfficeAdapter
{
    private readonly IEventWriter _events;
    private readonly WindowsOptions _options;
    private Dictionary<IntPtr, string> _baseline = new();
    private HashSet<IntPtr> _baselineDialogs = new();

    public WindowsOfficeAdapter(IEventWriter events, WindowsOptions options)
    {
        _events = events;
        _options = options;
    }

    public Task PrepareForOpenAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        _baseline = NativeMethods.GetVisibleWindows()
            .Where(w => OfficeDialogs.IsOfficeFrame(w.ClassName))
            .ToDictionary(w => w.Handle, w => w.Title);
        _baselineDialogs = OfficeDialogs.CurrentDialogs();
        return Task.CompletedTask;
    }

    public async Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        var expectedClass = file.Kind == DocumentKind.Excel ? OfficeDialogs.ExcelClass : OfficeDialogs.PowerPointClass;
        var baseName = StripExtension(file.Name);
        var deadline = DateTime.UtcNow + _options.OfficeOpenTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (IntPtr Handle, string Title, int Score) best = (IntPtr.Zero, "", 0);
            foreach (var w in NativeMethods.GetVisibleWindows())
            {
                if (!string.Equals(w.ClassName, expectedClass, StringComparison.Ordinal)) continue;
                var score = TitleScore(w.Title, file.Name, baseName);
                if (score == 0) continue;
                if (_baseline.TryGetValue(w.Handle, out var before) && !IsReplacedBlank(before, w.Title, score, file.Name, baseName))
                    continue;
                if (score > best.Score) best = (w.Handle, w.Title, score);
            }

            if (best.Score > 0)
                return new OfficeSession(best.Handle, best.Title, file.Kind);

            // "File in use" / "update links?" can appear before (or instead of) the document window.
            var dismissed = OfficeDialogs.TryDismissOpenPrompts(0, _baselineDialogs);
            if (dismissed is not null)
                await _events.WriteAsync(AgentEvent.Info("office.prompt", $"Answered open prompt {dismissed}"), cancellationToken)
                    .ConfigureAwait(false);

            Thread.Sleep(300);
        }

        var alreadyOpen = _baseline.Values.Any(t => TitleScore(t, file.Name, baseName) == 2);
        if (alreadyOpen)
            throw new AgentOperationException(ErrorCodes.OfficeOpenTimeout,
                $"'{file.Name}'이(가) 에이전트 실행 전부터 열려 있었습니다. 사용자 문서는 건드리지 않으므로, 그 창을 닫고 다시 실행하세요.");

        // Which windows did appear (e.g. a Protected View window of another class) - makes one run diagnostic.
        var appeared = NativeMethods.GetVisibleWindows()
            .Where(w => !_baseline.ContainsKey(w.Handle) && w.Title.Contains(baseName, StringComparison.Ordinal))
            .Select(w => $"{w.ClassName}:\"{w.Title}\"")
            .Take(5)
            .ToList();
        throw new AgentOperationException(ErrorCodes.OfficeOpenTimeout,
            $"Office window ({expectedClass}) did not appear for {file.Name} within {_options.OfficeOpenTimeout.TotalSeconds:0}s." +
            (appeared.Count > 0 ? $" New windows with that name: {string.Join(", ", appeared)}" : ""));
    }

    public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        var pid = NativeMethods.ProcessIdOf(session.WindowHandle);
        try
        {
            var root = Uia.FromHandle(session.WindowHandle);
            if (root.TryGetCurrentPattern(WindowPattern.Pattern, out var p))
                ((WindowPattern)p).Close();
        }
        catch (Exception ex)
        {
            // Closing is best-effort; report but never force-kill.
            throw new AgentOperationException(ErrorCodes.OfficeCloseTimeout, $"Close request failed: {ex.Message}", ex);
        }

        var deadline = DateTime.UtcNow + _options.CloseTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsGone(session.WindowHandle)) return Task.CompletedTask;
            // A save/discard prompt can block closing; the agent never edited the file, so discard.
            OfficeDialogs.TryDismissSavePrompt(session.WindowHandle, pid);
            if (IsGone(session.WindowHandle)) return Task.CompletedTask;
            Thread.Sleep(300);
        }

        throw new AgentOperationException(ErrorCodes.OfficeCloseTimeout,
            $"\"{session.Title}\" is still open {_options.CloseTimeout.TotalSeconds:0}s after the close request (a prompt may be waiting).");
    }

    /// <summary>2 = the title starts with this file (strong), 1 = contains its base name, 0 = no match.</summary>
    private static int TitleScore(string title, string fileName, string baseName)
    {
        if (string.IsNullOrEmpty(title)) return 0;
        if (title.StartsWith(fileName, StringComparison.OrdinalIgnoreCase)
            || title.StartsWith(baseName + " - ", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith(baseName + " [", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith(baseName + "  [", StringComparison.OrdinalIgnoreCase))
            return 2;
        return title.Contains(baseName, StringComparison.Ordinal) ? 1 : 0;
    }

    /// <summary>A pre-existing window is ours only if Office replaced a blank document in it with this file.</summary>
    private static bool IsReplacedBlank(string titleBefore, string titleNow, int scoreNow, string fileName, string baseName) =>
        scoreNow == 2
        && !string.Equals(titleBefore, titleNow, StringComparison.Ordinal)
        && TitleScore(titleBefore, fileName, baseName) == 0;

    private static bool IsGone(IntPtr handle) => !NativeMethods.Exists(handle) || !NativeMethods.IsVisible(handle);

    private static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
