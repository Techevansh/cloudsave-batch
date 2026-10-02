using System.Windows;
using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Opens the existing add-in and runs its structure analysis — the Stage C risk gate.
///
/// The ribbon button ("PPTX analyzer" / "Excel analyzer") is found in the Office window
/// subtree. The task-pane "구조 분석 시작" button lives in an Office.js WebView2 whose UIA
/// tree is not reliably exposed under the Office window; the proven technique (and the fix
/// for the v2.5 regression) is to scan from the DESKTOP ROOT and filter candidates to the
/// Office window's bounds. See Windows/README_ADAPTERS.md.
/// </summary>
internal sealed class WindowsAnalyzerAdapter : IAnalyzerAdapter
{
    private readonly IEventWriter _events;
    private readonly WindowsOptions _options;

    public WindowsAnalyzerAdapter(IEventWriter events, WindowsOptions options)
    {
        _events = events;
        _options = options;
    }

    public async Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        PrepareWindow(session);
        TryExitProtectedView(session, cancellationToken);

        if (FindStartButton(session, cancellationToken) is not null)
        {
            await _events.WriteAsync(AgentEvent.Info("analyzer.pane", "Task pane already open"), cancellationToken).ConfigureAwait(false);
            return;
        }

        TrySelectHomeTab(session, cancellationToken);
        var label = session.Kind == DocumentKind.Excel ? Strings.ExcelAnalyzer : Strings.PptxAnalyzer;

        // Phase 1: find + invoke the ribbon button (it can load a few seconds after the window).
        var deadline = DateTime.UtcNow + _options.AnalyzerOpenTimeout;
        var invoked = false;
        var lastCount = -1;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindStartButton(session, cancellationToken) is not null)
            {
                await _events.WriteAsync(AgentEvent.Info("analyzer.pane", "Task pane appeared"), cancellationToken).ConfigureAwait(false);
                return;
            }

            var candidates = FindRibbonAnalyzer(session, label, cancellationToken);
            if (candidates.Count != lastCount)
            {
                await _events.WriteAsync(AgentEvent.Info("analyzer.candidates", $"{label} count={candidates.Count}"), cancellationToken).ConfigureAwait(false);
                lastCount = candidates.Count;
            }

            var button = candidates.FirstOrDefault(c => c.ControlType == "ControlType.Button") ?? candidates.FirstOrDefault();
            if (button is not null)
            {
                await _events.WriteAsync(AgentEvent.Info("analyzer.open", $"{label} via {button.ControlType}"), cancellationToken).ConfigureAwait(false);
                Uia.Activate(button);
                invoked = true;
                break;
            }

            NativeMethods.BringToForeground(session.WindowHandle);
            Thread.Sleep(700);
        }

        if (!invoked)
            throw new AgentOperationException(ErrorCodes.AnalyzerButtonNotFound,
                $"Analyzer ribbon button not found: {label}.");

        // Phase 2: wait for the task-pane start button to expose. The Office.js pane
        // is a WebView2 whose accessibility tree is not built until prompted, so nudge
        // it awake (WM_GETOBJECT) and keep the Office window foreground on each poll.
        deadline = DateTime.UtcNow + _options.AnalyzerOpenTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeMethods.BringToForeground(session.WindowHandle);
            WakeTaskPane(session);
            if (FindStartButton(session, cancellationToken) is not null)
            {
                await _events.WriteAsync(AgentEvent.Info("analyzer.pane", "Task pane ready"), cancellationToken).ConfigureAwait(false);
                return;
            }
            Thread.Sleep(500);
        }

        await DumpTaskPaneDiagnosticsAsync(session, cancellationToken).ConfigureAwait(false);
        throw new AgentOperationException(ErrorCodes.AnalyzerTaskpaneTimeout,
            "Analyzer task pane did not expose the start button in time.");
    }

    public async Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        var start = FindStartButton(session, cancellationToken)
            ?? throw new AgentOperationException(ErrorCodes.AnalysisButtonNotFound, "Structure-analysis start button not visible.");

        WakeTaskPane(session);
        var baseline = TopLevelTitles();
        Uia.Activate(start);
        await _events.WriteAsync(AgentEvent.Info("analysis.start", session.Title), cancellationToken).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + _options.AnalysisTimeout;
        var seenWorking = false;
        var seenAuth = false;
        var reported = new HashSet<string>(StringComparer.Ordinal);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startVisible = FindStartButton(session, cancellationToken) is not null;
            var busyVisible = FindInOfficeBounds(session, h => h.Name.Contains(Strings.Analyzing, StringComparison.Ordinal), cancellationToken).Count > 0;
            var doneVisible = FindInOfficeBounds(session, h => h.Name.Contains(Strings.ReportDone, StringComparison.Ordinal), cancellationToken).Count > 0;
            var stopVisible = FindInOfficeBounds(session, h => h.Name.Contains(Strings.InspectStopped, StringComparison.Ordinal), cancellationToken).Count > 0;

            if (busyVisible && !seenWorking)
            {
                seenWorking = true;
                await _events.WriteAsync(AgentEvent.Info("analysis.working", session.Title), cancellationToken).ConfigureAwait(false);
            }

            // A new top-level window is almost certainly a sign-in / consent dialog the user must complete.
            foreach (var w in NativeMethods.GetVisibleWindows())
            {
                var key = w.Handle.ToString();
                if (baseline.Contains(key) || reported.Contains(key)) continue;
                if (string.IsNullOrWhiteSpace(w.Title) || w.Title.Contains("cloudsave-agent", StringComparison.OrdinalIgnoreCase)) continue;
                reported.Add(key);
                seenAuth = true;
                await _events.WriteAsync(AgentEvent.Info("auth.window",
                    $"Sign-in/consent window: \"{w.Title}\" — complete it manually; no password is stored"), cancellationToken).ConfigureAwait(false);
            }

            if (doneVisible)
                return AnalysisResult.Completed(seenAuth);
            if (stopVisible)
                return AnalysisResult.Failure(ErrorCodes.AnalysisFailed, "Analyzer reported a stop/error state.");

            // The add-in restores the start button in finally{} after success or failure.
            if (startVisible && (seenWorking || seenAuth))
                return AnalysisResult.Completed(seenAuth);

            Thread.Sleep(400);
        }

        var code = seenAuth ? ErrorCodes.AuthWaitTimeout : ErrorCodes.AnalysisTimeout;
        return AnalysisResult.Failure(code, "Analysis did not complete in time (a sign-in window may still be waiting).");
    }

    // ---- discovery helpers --------------------------------------------------

    private void PrepareWindow(OfficeSession session)
    {
        Uia.TryMaximize(Uia.FromHandle(session.WindowHandle));
        NativeMethods.BringToForeground(session.WindowHandle);
        Thread.Sleep(1000);
    }

    private Rect OfficeBounds(OfficeSession session)
    {
        try
        {
            var r = Uia.FromHandle(session.WindowHandle).Current.BoundingRectangle;
            if (r is { Width: > 0, Height: > 0 }) return r;
        }
        catch { /* fall through */ }
        return Rect.Empty;
    }

    /// <summary>Desktop-root scan, filtered to the Office window bounds — the key task-pane technique.</summary>
    private List<UiaHit> FindInOfficeBounds(OfficeSession session, Func<UiaHit, bool> match, CancellationToken ct)
    {
        var bounds = OfficeBounds(session);
        return Uia.Collect(Uia.Root, h =>
        {
            if (h.IsOffscreen || h.Rect is not { Width: > 0, Height: > 0 }) return false;
            if (!bounds.IsEmpty)
            {
                var cx = h.Rect.X + h.Rect.Width / 2;
                var cy = h.Rect.Y + h.Rect.Height / 2;
                if (!bounds.Contains(cx, cy)) return false;
            }
            return match(h);
        }, ct);
    }

    // Contains, not exact: the HTML button reads "🔍 구조 분석 시작" (emoji + text), so its
    // accessible name may carry the emoji/whitespace. Match on the core phrase.
    private UiaHit? FindStartButton(OfficeSession session, CancellationToken ct) =>
        FindInOfficeBounds(session, h => h.Name.Contains(Strings.AnalysisStart, StringComparison.Ordinal), ct).FirstOrDefault();

    /// <summary>Prompt the Office window and its WebView2 child windows to build their accessibility trees.</summary>
    private static void WakeTaskPane(OfficeSession session)
    {
        NativeMethods.WakeAccessibility(session.WindowHandle);
        foreach (var c in NativeMethods.GetChildWindows(session.WindowHandle))
        {
            var cls = c.ClassName;
            if (cls.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("WebView", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("Widget", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("EdgeWebView", StringComparison.OrdinalIgnoreCase))
            {
                NativeMethods.WakeAccessibility(c.Handle);
            }
        }
    }

    /// <summary>On task-pane timeout, log what IS exposed near the pane so the real cause is visible from one run.</summary>
    private async Task DumpTaskPaneDiagnosticsAsync(OfficeSession session, CancellationToken ct)
    {
        try
        {
            var b = OfficeBounds(session);
            await _events.WriteAsync(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout,
                $"office bounds = X={b.X:0} Y={b.Y:0} W={b.Width:0} H={b.Height:0}"), ct).ConfigureAwait(false);

            var kids = NativeMethods.GetChildWindows(session.WindowHandle)
                .Select(k => k.ClassName)
                .Where(c => c.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
                         || c.Contains("WebView", StringComparison.OrdinalIgnoreCase)
                         || c.Contains("Widget", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
            await _events.WriteAsync(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout,
                $"web/child window classes: {(kids.Count > 0 ? string.Join(", ", kids) : "(none)")}"), ct).ConfigureAwait(false);

            // Named elements in the right portion of the Office window (where the pane sits).
            var region = b.IsEmpty ? System.Windows.Rect.Empty
                : new System.Windows.Rect(b.X + b.Width * 0.50, b.Y, b.Width * 0.50, b.Height);
            var named = Uia.Collect(Uia.Root, h =>
            {
                if (h.IsOffscreen || string.IsNullOrWhiteSpace(h.Name) || h.Rect is not { Width: > 0, Height: > 0 }) return false;
                if (region.IsEmpty) return true;
                return region.Contains(h.Rect.X + h.Rect.Width / 2, h.Rect.Y + h.Rect.Height / 2);
            }, ct);

            await _events.WriteAsync(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout,
                $"named elements in pane region: {named.Count}"), ct).ConfigureAwait(false);
            foreach (var h in named.Take(30))
            {
                await _events.WriteAsync(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout,
                    $"  [{h.ControlType}] \"{h.Name}\" rect={h.Rect.X:0},{h.Rect.Y:0},{h.Rect.Width:0},{h.Rect.Height:0}"), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await _events.WriteAsync(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout,
                $"diagnostics failed: {ex.Message}"), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private List<UiaHit> FindRibbonAnalyzer(OfficeSession session, string label, CancellationToken ct)
    {
        var root = Uia.FromHandle(session.WindowHandle);
        var exact = Uia.Collect(root, h => h.Name == label, ct);
        if (exact.Count > 0) return exact;
        // Looser match: the add-in name plus "analy".
        var token = session.Kind == DocumentKind.Excel ? "excel" : "pptx";
        return Uia.Collect(root, h =>
            h.Name.Contains("analy", StringComparison.OrdinalIgnoreCase) &&
            h.Name.Contains(token, StringComparison.OrdinalIgnoreCase), ct);
    }

    private void TrySelectHomeTab(OfficeSession session, CancellationToken ct)
    {
        try
        {
            var tab = Uia.Collect(Uia.FromHandle(session.WindowHandle), h =>
                h.ControlType == "ControlType.TabItem" &&
                (h.AutomationId == "TabHome" || h.Name == Strings.HomeTab || h.Name == Strings.HomeTabEn), ct).FirstOrDefault();
            if (tab is not null && tab.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
            {
                ((SelectionItemPattern)p).Select();
                Thread.Sleep(400);
            }
        }
        catch { /* best effort */ }
    }

    private void TryExitProtectedView(OfficeSession session, CancellationToken ct)
    {
        if (!_options.EnableProtectedViewEdit) return;
        try
        {
            var btn = Uia.Collect(Uia.FromHandle(session.WindowHandle), h =>
                !h.IsOffscreen && (h.Name == Strings.EnableEditing || h.Name == Strings.EnableEditingEn), ct).FirstOrDefault();
            if (btn is not null)
            {
                _events.WriteAsync(AgentEvent.Info("office.protected_view", "Clicking Enable Editing"), ct).GetAwaiter().GetResult();
                Uia.Activate(btn);
                Thread.Sleep(1500);
            }
        }
        catch { /* best effort */ }
    }

    private static HashSet<string> TopLevelTitles() =>
        NativeMethods.GetVisibleWindows().Select(w => w.Handle.ToString()).ToHashSet(StringComparer.Ordinal);
}
