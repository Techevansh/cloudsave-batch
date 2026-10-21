namespace CloudSave.Agent.Core;

/// <summary>
/// The existing add-ins' own task-pane vocabulary. Identical in cloudsave/taskpane.js
/// (PowerPoint) and cloudsave-excel/excel.js (Excel): syncToCloud() logs one
/// "✅ ..." or "❌ ..." line per destination after BOTH uploads settle, then
/// "─── 분석 리포트 완성 ───"; an exception logs "❌ 검사 중단: ..." instead.
/// The start button reads "🔍 구조 분석 시작" and "분석 중..." while running.
/// </summary>
public static class AnalyzerPaneText
{
    public const string StartButton = "구조 분석 시작";
    public const string Working = "분석 중...";
    public const string ReportDone = "분석 리포트 완성";
    public const string Stopped = "검사 중단";
    public const string UploadOk = "✅";
    public const string UploadFailed = "❌";
}

/// <summary>What the task pane shows at one instant (built from the UIA names inside the pane).</summary>
public sealed record PaneSnapshot(
    bool StartVisible,
    bool Working,
    bool Done,
    string? StopText,
    IReadOnlyList<string> FailureLines,
    int SuccessCount)
{
    public static PaneSnapshot FromTexts(IEnumerable<string> texts)
    {
        var list = texts
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var stop = list.FirstOrDefault(t => t.Contains(AnalyzerPaneText.Stopped, StringComparison.Ordinal));
        var failures = list
            .Where(t => t.Contains(AnalyzerPaneText.UploadFailed, StringComparison.Ordinal)
                        && !t.Contains(AnalyzerPaneText.Stopped, StringComparison.Ordinal))
            .ToList();

        return new PaneSnapshot(
            StartVisible: list.Any(t => t.Contains(AnalyzerPaneText.StartButton, StringComparison.Ordinal)),
            Working: list.Any(t => t.Contains(AnalyzerPaneText.Working, StringComparison.Ordinal)),
            Done: list.Any(t => t.Contains(AnalyzerPaneText.ReportDone, StringComparison.Ordinal)),
            StopText: stop,
            FailureLines: failures,
            SuccessCount: list.Count(t => t.Contains(AnalyzerPaneText.UploadOk, StringComparison.Ordinal)));
    }

    /// <summary>True when no result text (from this or a previous attempt) is showing.</summary>
    public bool HasNoResultText => !Done && StopText is null && FailureLines.Count == 0 && SuccessCount == 0;

    /// <summary>Identity of the result texts shown, to tell a new result from a previous attempt's.</summary>
    public string ResultSignature =>
        $"{Done}|{StopText}|{SuccessCount}|{string.Join("|", FailureLines)}";
}

public enum AnalysisVerdict
{
    /// <summary>Keep polling.</summary>
    Pending,
    /// <summary>Report done and at least one "✅" destination line, no "❌".</summary>
    Succeeded,
    /// <summary>The run finished but no per-destination line could be read (a11y lag); treated as done, flagged.</summary>
    SucceededUnverified,
    /// <summary>Report done with at least one "❌" destination line: an upload did not happen.</summary>
    UploadFailed,
    /// <summary>The add-in stopped with an error ("검사 중단") or refused to start.</summary>
    Stopped,
}

/// <summary>
/// Interprets successive <see cref="PaneSnapshot"/>s after ONE click of the start button.
/// Pure logic (no UIA) so the completion rules are unit-tested in Core.
///
/// The key rule: a file only counts as uploaded when the pane says so per destination.
/// "분석 리포트 완성" alone is not enough, because the add-in prints it even when an
/// upload failed (Promise.allSettled), with the failure on a "❌" line above it.
/// </summary>
public sealed class AnalysisTracker
{
    /// <summary>Polls to wait for a lone "❌" line (no work, no report) before calling it a refusal.</summary>
    public const int RefusalPolls = 3;

    /// <summary>Polls to wait, after the start button returns, for result lines to become readable.</summary>
    public const int UnverifiedPolls = 8;

    private bool _seenWorking;
    private bool _armed;
    private string? _baselineSignature;
    private int _loneFailurePolls;
    private int _restoredPolls;

    public bool SeenWorking => _seenWorking;
    public string? Detail { get; private set; }
    public IReadOnlyList<string> Failures { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// The pane as it was just BEFORE the click. A clean pane arms the tracker at once, so a run
    /// that finishes between two polls is still recognised. A pane still showing a previous
    /// attempt's results keeps the tracker disarmed until the new run is evidently underway.
    /// </summary>
    public void Baseline(PaneSnapshot beforeClick)
    {
        _baselineSignature = beforeClick.ResultSignature;
        if (beforeClick.HasNoResultText) _armed = true;
    }

    public AnalysisVerdict Observe(PaneSnapshot s)
    {
        if (s.Working) _seenWorking = true;

        // Results only count once they can belong to THIS click: the run was seen working, the old
        // results were cleared, or the results differ from what was on screen before the click.
        if (_seenWorking || s.HasNoResultText
            || (_baselineSignature is not null && !string.Equals(s.ResultSignature, _baselineSignature, StringComparison.Ordinal)))
            _armed = true;
        if (!_armed) return AnalysisVerdict.Pending;

        if (s.StopText is not null)
        {
            Detail = s.StopText;
            return AnalysisVerdict.Stopped;
        }

        if (s.Done)
        {
            if (s.FailureLines.Count > 0)
            {
                Failures = s.FailureLines;
                Detail = string.Join(" | ", s.FailureLines);
                return AnalysisVerdict.UploadFailed;
            }
            return s.SuccessCount > 0 ? AnalysisVerdict.Succeeded : AnalysisVerdict.SucceededUnverified;
        }

        // A "❌" line with no work and no report = the add-in refused to start
        // (e.g. "검사 항목을 하나 이상 선택해주세요.").
        if (!s.Working && s.FailureLines.Count > 0)
        {
            if (++_loneFailurePolls >= RefusalPolls)
            {
                Detail = s.FailureLines[0];
                return AnalysisVerdict.Stopped;
            }
        }
        else
        {
            _loneFailurePolls = 0;
        }

        // The add-in restores the start button in finally{}. If it ran and the button is back
        // but no result line is readable, allow a few polls for the a11y tree, then accept it
        // as done-but-unverified (reported to the user, never silently counted as verified).
        if (_seenWorking && s.StartVisible && !s.Working)
        {
            if (++_restoredPolls >= UnverifiedPolls) return AnalysisVerdict.SucceededUnverified;
        }
        else
        {
            _restoredPolls = 0;
        }

        return AnalysisVerdict.Pending;
    }
}
