using System.Text.RegularExpressions;
using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Reads and navigates the File Explorer window the user already opened, through UI Automation only.
///   * Start window: the most recently used (Z order) Explorer window that is NOT the agent's own folder.
///   * Rows: Explorer item elements (class "UIItem"), so ribbon galleries and other lists are never
///     mistaken for files; classified from the row's own cell texts (type / size) via Core.
///   * Completeness: the listing is checked against Explorer's own "N개 항목" count; if UIA exposed
///     fewer rows, the list is scrolled and re-read.
///   * Clicks: Explorer is brought to the front, the row scrolled into view, and the click is sent
///     only if a hit-test proves the point belongs to Explorer (never a covered/foreign window).
///   * Navigation: entering a folder must visibly change the location; Back must verifiably return
///     to the exact parent, otherwise EXPLORER_LOCATION_LOST stops the run.
/// No filesystem access on U:, no SendKeys, no guessed coordinates.
/// </summary>
internal sealed class WindowsExplorerAdapter : IExplorerAdapter
{
    private const string ExplorerClass = "CabinetWClass";
    private static readonly ControlType[] RowTypes = { ControlType.DataItem, ControlType.ListItem };
    private static readonly ControlType[] RowAndListTypes = { ControlType.DataItem, ControlType.ListItem, ControlType.List };
    private static readonly ControlType[] NavButtonTypes = { ControlType.Button, ControlType.SplitButton };

    // Explorer status bar item count: "37개 항목" / "항목 37개" / "37 items".
    private static readonly Regex ItemCount = new(
        @"^\s*(?:(?<n>\d[\d,]*)\s*개\s*항목|항목\s*(?<n>\d[\d,]*)\s*개|(?<n>\d[\d,]*)\s+items?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IEventWriter _events;
    private readonly WindowsOptions _options;
    private readonly Stack<Location> _back = new();
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private IntPtr _hwnd;
    private bool _structureLogged;

    /// <summary>Where Explorer is: address-bar path (if readable), window title, and the listing signature.</summary>
    private sealed record Location(string? Address, string Title, string Rows)
    {
        public string Display => Address ?? Title;
    }

    public WindowsExplorerAdapter(IEventWriter events, WindowsOptions options)
    {
        _events = events;
        _options = options;
    }

    // ---- IExplorerAdapter ------------------------------------------------------

    public async Task<string> GetStartContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chosen = IntPtr.Zero;
        var explorers = 0;
        foreach (var w in NativeMethods.GetVisibleWindows())      // Z order: most recently used first
        {
            if (!string.Equals(w.ClassName, ExplorerClass, StringComparison.Ordinal)) continue;
            explorers++;

            string? ignored = null;
            var rowCount = -1;
            if (IsSpecialLocation(w.Title))
            {
                // "내 PC" / "홈" etc. are not a folder to process; recursing from there would walk whole drives.
                ignored = "not a folder";
            }
            else
            {
                try
                {
                    var rows = RowHits(w.Handle, cancellationToken);
                    rowCount = rows.Count;
                    if (rows.Any(r => IsAgentFile(r.Name))) ignored = "the agent's own folder";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { ignored = "unreadable: " + ex.Message; }
            }

            var pick = ignored is null && chosen == IntPtr.Zero;
            if (pick) chosen = w.Handle;
            await _events.WriteAsync(AgentEvent.Info("explorer.candidate",
                $"\"{w.Title}\" rows={rowCount}{(pick ? " -> chosen" : ignored is null ? "" : $" (ignored: {ignored})")}"),
                cancellationToken).ConfigureAwait(false);
        }

        if (chosen == IntPtr.Zero)
            throw new AgentOperationException(ErrorCodes.ExplorerNotFound, explorers == 0
                ? "열려 있는 탐색기 창이 없습니다. 처리할 폴더를 탐색기로 연 뒤 실행하세요."
                : "에이전트 폴더 외에 처리할 폴더 창이 없습니다. 처리할 폴더를 별도의 탐색기 창으로 열어 두세요.");

        _hwnd = chosen;
        _back.Clear();
        return ReadLocation(cancellationToken, withRows: false).Display;
    }

    public async Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken)
    {
        EnsureWindow();
        var hits = RowHits(_hwnd, cancellationToken);

        // Completeness: never silently miss files that UIA did not expose.
        var expected = StatusItemCount(cancellationToken);
        if (expected is int n && hits.Count < n)
        {
            hits = Union(hits, ScrollCollect(cancellationToken));
            if (hits.Count < n)
                await WarnOnceAsync("explorer.incomplete", logicalFolder, ErrorCodes.ExplorerListIncomplete,
                    $"탐색기에는 {n}개 항목이 있지만 {hits.Count}개만 읽혔습니다: {logicalFolder}. 창을 크게 하고 '자세히' 보기로 다시 실행해 주세요.",
                    cancellationToken).ConfigureAwait(false);
        }

        var items = new List<ExplorerItem>(hits.Count);
        var hiddenOffice = 0;
        var rowsWithEvidence = 0;
        foreach (var h in hits)
        {
            var texts = DocumentClassifier.IsSupportedOffice(h.Name) ? null : Uia.CellTexts(h.Element);
            if (texts is not null && texts.Any(t => !string.Equals(t.Trim(), h.Name.Trim(), StringComparison.Ordinal)))
                rowsWithEvidence++;

            var c = DocumentClassifier.ClassifyRow(h.Name, texts);
            if (c.LikelyHiddenOfficeFile) hiddenOffice++;
            items.Add(new ExplorerItem(h.Name, LogicalPaths.Combine(logicalFolder, h.Name), c.IsFolder, c.Kind));
        }

        if (hiddenOffice > 0)
            await WarnOnceAsync("explorer.extensions", logicalFolder, ErrorCodes.ExplorerExtensionHidden,
                $"확장명이 숨겨진 Office 파일 {hiddenOffice}개를 건너뜁니다 ({logicalFolder}). 탐색기 [보기] → '파일 확장명'을 켜고 다시 실행하세요.",
                cancellationToken).ConfigureAwait(false);

        var unclassified = items.Count(i => i.Kind == DocumentKind.Other);
        if (unclassified > 0 && rowsWithEvidence == 0)
            await WarnOnceAsync("explorer.view", "*", ErrorCodes.ExplorerListIncomplete,
                "탐색기를 '자세히' 보기로 바꾸면 폴더와 파일을 더 정확히 구분합니다 (점이 들어간 폴더 이름 등).",
                cancellationToken).ConfigureAwait(false);

        return items;
    }

    public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken)
    {
        EnsureWindow();
        var before = ReadLocation(cancellationToken, withRows: true);
        OpenRow(folder.Name, cancellationToken);

        var deadline = DateTime.UtcNow + _options.EnterFolderTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(300);
            if (Moved(ReadLocation(cancellationToken, withRows: false), before))
            {
                WaitSettled(cancellationToken);
                _back.Push(before);
                return Task.CompletedTask;
            }
        }

        // Address/title unchanged (e.g. a child folder named like its parent): decide by the listing itself.
        WaitSettled(cancellationToken);
        if (!string.Equals(ReadLocation(cancellationToken, withRows: true).Rows, before.Rows, StringComparison.Ordinal))
        {
            _back.Push(before);
            return Task.CompletedTask;
        }

        throw new AgentOperationException(ErrorCodes.ExplorerNavigationTimeout,
            $"폴더 '{folder.Name}'(으)로 이동하지 못했습니다 (탐색기 화면이 바뀌지 않음).");
    }

    public Task GoBackAsync(CancellationToken cancellationToken)
    {
        EnsureWindow();
        if (_back.Count == 0)
            throw Lost("돌아갈 상위 폴더 기록이 없습니다.");

        var expected = _back.Pop();
        var child = ReadLocation(cancellationToken, withRows: false);

        if (PressNav(back: true, cancellationToken) && WaitArrive(expected, cancellationToken))
            return Task.CompletedTask;

        // Back did not bring us there. Only if Explorer is verifiably still in the child folder,
        // use Up once (Up always goes to the parent, independent of history).
        var now = ReadLocation(cancellationToken, withRows: false);
        if (!Moved(now, child) && PressNav(back: false, cancellationToken) && WaitArrive(expected, cancellationToken))
            return Task.CompletedTask;

        throw Lost($"탐색기가 '{expected.Display}'(으)로 돌아오지 않았습니다 (현재: '{ReadLocation(cancellationToken, withRows: false).Display}').");
    }

    public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        EnsureWindow();
        OpenRow(file.Name, cancellationToken);
        return Task.CompletedTask;
    }

    // ---- rows ------------------------------------------------------------------

    /// <summary>
    /// The Explorer item rows of a window. Explorer's item view (class "UIItemsView") holds rows of
    /// class "UIItem"; scoping to them keeps ribbon galleries and other lists out. Offscreen rows are
    /// kept (they are clicked only after scrolling into view). If this Explorer build does not expose
    /// those classes, fall back to the earlier proven heuristic (visible list rows).
    /// </summary>
    private List<UiaHit> RowHits(IntPtr hwnd, CancellationToken ct)
    {
        var all = Uia.CollectByType(Uia.FromHandle(hwnd), RowAndListTypes, _ => true, ct);
        var hasItemsView = all.Any(h => h.ClassName == Strings.ExplorerItemsViewClass);
        var rows = all.Where(h => h.ControlType != "ControlType.List" && !string.IsNullOrWhiteSpace(h.Name)).ToList();
        var items = rows.Where(h => h.ClassName == Strings.ExplorerRowClass).ToList();

        if (items.Count > 0 || hasItemsView) return Dedupe(items);

        if (!_structureLogged && rows.Count > 0)
        {
            _structureLogged = true;
            var classes = string.Join(", ", rows.Select(r => $"{r.ControlType}/{r.ClassName}").Distinct().Take(10));
            _events.WriteAsync(AgentEvent.Info("explorer.structure",
                $"Explorer item classes not found; using visible-row fallback. Seen: {classes}"), CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        return Dedupe(rows.Where(h => h.Rect is { Width: > 0, Height: > 0 }).ToList());
    }

    private static List<UiaHit> Dedupe(List<UiaHit> hits)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return hits.Where(h => seen.Add(h.ControlType + "|" + h.Name)).ToList();
    }

    private static List<UiaHit> Union(List<UiaHit> a, List<UiaHit> b) => Dedupe(a.Concat(b).ToList());

    private static readonly HashSet<string> SpecialLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        "내 PC", "This PC", "홈", "Home", "빠른 실행", "즐겨찾기", "Quick access", "네트워크", "Network",
        "휴지통", "Recycle Bin", "제어판", "Control Panel", "라이브러리", "Libraries", "갤러리", "Gallery",
    };

    /// <summary>Explorer shell locations that are not a folder of files (title may carry " - 파일 탐색기").</summary>
    private static bool IsSpecialLocation(string title)
    {
        var t = title.Trim();
        foreach (var suffix in new[] { " - 파일 탐색기", " - File Explorer" })
            if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) t = t[..^suffix.Length].Trim();
        return SpecialLocations.Contains(t);
    }

    private static bool IsAgentFile(string name) =>
        name.StartsWith("cloudsave-agent", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CloudSave.Agent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Explorer's own item count from the status bar, or null if the status bar is hidden/unreadable.</summary>
    private int? StatusItemCount(CancellationToken ct)
    {
        try
        {
            foreach (var bar in Uia.CollectByType(Uia.FromHandle(_hwnd), new[] { ControlType.StatusBar }, _ => true, ct, attempts: 2))
            {
                var texts = Uia.Collect(bar.Element, _ => true, ct, attempts: 2).Select(h => h.Name).Append(bar.Name);
                foreach (var t in texts)
                {
                    var m = ItemCount.Match(t ?? "");
                    if (m.Success && int.TryParse(m.Groups["n"].Value.Replace(",", ""), out var n)) return n;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* no status bar: skip the check */ }
        return null;
    }

    /// <summary>Scroll the item view top to bottom and collect rows at each position (for lists UIA only partly exposes).</summary>
    private List<UiaHit> ScrollCollect(CancellationToken ct, string? stopAtName = null)
    {
        var found = new List<UiaHit>();
        var sp = ItemsViewScroll(ct);
        if (sp is null) return found;
        try
        {
            var step = Math.Max(5.0, sp.Current.VerticalViewSize * 0.8);
            for (var pct = 0.0; ; pct += step)
            {
                ct.ThrowIfCancellationRequested();
                sp.SetScrollPercent(ScrollPattern.NoScroll, Math.Min(100.0, pct));
                Thread.Sleep(300);
                var rows = RowHits(_hwnd, ct);
                found = Union(found, rows);
                if (stopAtName is not null && rows.Any(r => r.Name == stopAtName)) return found;
                if (pct >= 100.0) break;
            }
            if (stopAtName is null) sp.SetScrollPercent(ScrollPattern.NoScroll, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* best effort */ }
        return found;
    }

    private ScrollPattern? ItemsViewScroll(CancellationToken ct)
    {
        var view = Uia.CollectByType(Uia.FromHandle(_hwnd), new[] { ControlType.List },
            h => h.ClassName == Strings.ExplorerItemsViewClass, ct, attempts: 2).FirstOrDefault();
        if (view is null || !view.Element.TryGetCurrentPattern(ScrollPattern.Pattern, out var p)) return null;
        var sp = (ScrollPattern)p;
        return sp.Current.VerticallyScrollable ? sp : null;
    }

    private UiaHit? FindRow(string name, CancellationToken ct) =>
        RowHits(_hwnd, ct).FirstOrDefault(h => h.Name == name);

    /// <summary>Double-click a row safely: front, scrolled into view, hit-tested. Retries a covered target twice.</summary>
    private void OpenRow(string name, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var hit = FindRow(name, ct)
                ?? ScrollCollect(ct, stopAtName: name).FirstOrDefault(h => h.Name == name)
                ?? throw new AgentOperationException(ErrorCodes.ExplorerRowNotFound, $"탐색기에서 '{name}' 항목을 찾지 못했습니다.");

            NativeMethods.ForceForeground(_hwnd);
            Thread.Sleep(150);
            if (Uia.TryScrollIntoView(hit.Element))
            {
                Thread.Sleep(250);
                hit = FindRow(name, ct) ?? hit;   // fresh rectangle after scrolling
            }

            try
            {
                Uia.Activate(hit, doubleClick: true, requireRoot: _hwnd);
                return;
            }
            catch (AgentOperationException ex) when (ex.Code == ErrorCodes.ExplorerClickBlocked && attempt < 3)
            {
                Thread.Sleep(500);   // a tooltip or popup was in the way; bring Explorer forward and retry
            }
        }
    }

    // ---- location / navigation ----------------------------------------------------

    private Location ReadLocation(CancellationToken ct, bool withRows)
    {
        var title = NativeMethods.TitleOf(_hwnd);
        string? address = null;
        try
        {
            var bar = Uia.CollectByType(Uia.FromHandle(_hwnd), new[] { ControlType.ToolBar },
                h => h.Name.StartsWith(Strings.AddressPrefix, StringComparison.Ordinal)
                     || h.Name.StartsWith(Strings.AddressPrefixEn, StringComparison.OrdinalIgnoreCase),
                ct, attempts: 2).FirstOrDefault();
            if (bar is not null)
            {
                var path = bar.Name[(bar.Name.IndexOf(':') + 1)..].Trim();
                if (path.Length > 0) address = path;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* address bar not exposed: title only */ }

        var rows = withRows
            ? RowSignature(ct)
            : "";
        return new Location(address, title, rows);
    }

    /// <summary>The listing as one comparable string; Office lock files ("~$...") are ignored because they come and go.</summary>
    private string RowSignature(CancellationToken ct) =>
        string.Join("\u001f", RowHits(_hwnd, ct)
            .Select(h => h.Name)
            .Where(n => !DocumentClassifier.IsOfficeTempFile(n))
            .OrderBy(n => n, StringComparer.Ordinal));

    private static bool Moved(Location now, Location before) =>
        now.Address is not null && before.Address is not null
            ? !string.Equals(now.Address, before.Address, StringComparison.OrdinalIgnoreCase)
            : !string.Equals(now.Title, before.Title, StringComparison.Ordinal);

    /// <summary>Wait until Explorer shows <paramref name="expected"/> (address, or title + identical listing).</summary>
    private bool WaitArrive(Location expected, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _options.BackTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(300);
            var now = ReadLocation(ct, withRows: false);
            if (now.Address is not null && expected.Address is not null)
            {
                if (!string.Equals(now.Address, expected.Address, StringComparison.OrdinalIgnoreCase)) continue;
                WaitSettled(ct);
                return true;
            }

            if (!string.Equals(now.Title, expected.Title, StringComparison.Ordinal)) continue;
            WaitSettled(ct);
            if (string.Equals(ReadLocation(ct, withRows: true).Rows, expected.Rows, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>After navigation, wait until two consecutive listings are identical (the view finished loading).</summary>
    private void WaitSettled(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _options.SettleTimeout;
        string? last = null;
        Thread.Sleep(300);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var sig = RowSignature(ct);
            if (last is not null && string.Equals(sig, last, StringComparison.Ordinal)) return;
            last = sig;
            Thread.Sleep(400);
        }
    }

    /// <summary>Invoke Explorer's Back (or Up) button. Returns false if it is not found or not enabled.</summary>
    private bool PressNav(bool back, CancellationToken ct)
    {
        try
        {
            var button = Uia.CollectByType(Uia.FromHandle(_hwnd), NavButtonTypes,
                h => !h.IsOffscreen && h.Rect is { Width: > 0, Height: > 0 } && IsNavButton(h.Name, back), ct)
                .FirstOrDefault();
            if (button is null) return false;
            Uia.Activate(button);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    private static bool IsNavButton(string name, bool back)
    {
        var n = name.Replace(" ", "", StringComparison.Ordinal);
        return back
            ? n.StartsWith(Strings.Back, StringComparison.Ordinal)
              || n.Contains("Alt+왼쪽", StringComparison.Ordinal)
              || n.Contains("Alt+Left", StringComparison.OrdinalIgnoreCase)
              || n.Equals(Strings.BackEn, StringComparison.OrdinalIgnoreCase)
              || n.StartsWith("Backto", StringComparison.OrdinalIgnoreCase)
              || n.StartsWith("Back(", StringComparison.OrdinalIgnoreCase)
            : n.StartsWith(Strings.Up, StringComparison.Ordinal)
              || n.Contains("상위폴더", StringComparison.Ordinal)
              || n.Contains("Alt+위쪽", StringComparison.Ordinal)
              || n.Contains("Alt+Up", StringComparison.OrdinalIgnoreCase)
              || n.Equals(Strings.UpEn, StringComparison.OrdinalIgnoreCase)
              || n.StartsWith("Upto", StringComparison.OrdinalIgnoreCase)
              || n.StartsWith("Up(", StringComparison.OrdinalIgnoreCase);
    }

    // ---- misc --------------------------------------------------------------------

    private void EnsureWindow()
    {
        if (_hwnd == IntPtr.Zero)
            throw new AgentOperationException(ErrorCodes.ExplorerNotFound, "Start Explorer window has not been selected.");
        if (!NativeMethods.Exists(_hwnd))
            throw Lost("처리 중이던 탐색기 창이 닫혔습니다.");
    }

    private static AgentOperationException Lost(string message) =>
        new(ErrorCodes.ExplorerLocationLost, message);

    private async Task WarnOnceAsync(string type, string scope, string code, string message, CancellationToken ct)
    {
        if (!_warned.Add(type + "|" + scope)) return;
        await _events.WriteAsync(AgentEvent.Warn(type, code, message), ct).ConfigureAwait(false);
    }
}
