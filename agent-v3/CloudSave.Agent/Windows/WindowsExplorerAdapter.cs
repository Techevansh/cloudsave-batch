using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Reads and navigates the File Explorer window the user already opened, through UI Automation only.
/// Faithful port of the proven PowerShell reader (explorer-walker / single-office-open):
///   * CabinetWClass windows, scored by rows + Office count,
///   * DataItem/ListItem descendants with a positive rectangle,
///   * classify via DocumentClassifier, with the UIA "file folder" type confirming dotted folders,
///   * activate via Invoke/Select/real-bounding-rect, enter by double-activate, Back via its button,
///   * re-read after navigation (elements are never cached across moves).
/// No filesystem access on U:.
/// </summary>
internal sealed class WindowsExplorerAdapter : IExplorerAdapter
{
    private const string ExplorerClass = "CabinetWClass";
    private readonly IEventWriter _events;
    private readonly WindowsOptions _options;
    private IntPtr _hwnd;

    public WindowsExplorerAdapter(IEventWriter events, WindowsOptions options)
    {
        _events = events;
        _options = options;
    }

    public Task<string> GetStartContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        (IntPtr Handle, string Title, int Score)? best = null;
        foreach (var h in Uia.FindWindows(ExplorerClass))
        {
            try
            {
                var rows = ReadRows(h, "", cancellationToken);
                var office = rows.Count(r => !r.IsFolder && r.Kind is DocumentKind.PowerPoint or DocumentKind.Excel);
                var folders = rows.Count(r => r.IsFolder);
                var score = rows.Count + office * 10 + folders * 2;
                if (best is null || score > best.Value.Score)
                    best = (h, NativeMethods.TitleOf(h), score);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* skip unreadable window */ }
        }

        if (best is null)
            throw new AgentOperationException(ErrorCodes.ExplorerNotFound,
                "No readable File Explorer window found. Open the desired start folder first.");

        _hwnd = best.Value.Handle;
        return Task.FromResult(best.Value.Title);
    }

    public Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken)
    {
        EnsureWindow();
        var rows = ReadRows(_hwnd, logicalFolder, cancellationToken);
        return Task.FromResult<IReadOnlyList<ExplorerItem>>(rows);
    }

    public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken)
    {
        EnsureWindow();
        var before = NativeMethods.TitleOf(_hwnd);
        var hit = FindRowHit(folder.Name, cancellationToken)
            ?? throw new AgentOperationException(ErrorCodes.ExplorerRowNotFound, $"Folder row not found: {folder.Name}");

        Uia.Activate(hit, doubleClick: true);

        var deadline = DateTime.UtcNow + _options.EnterFolderTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(NativeMethods.TitleOf(_hwnd), before, StringComparison.Ordinal))
                return Task.CompletedTask;
            Thread.Sleep(250);
        }
        // Some Explorer tabs keep a generic title; allow the view to settle rather than failing hard.
        Thread.Sleep(600);
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CancellationToken cancellationToken)
    {
        EnsureWindow();
        var root = Uia.FromHandle(_hwnd);
        var back = Uia.Collect(root, h =>
                !h.IsOffscreen && h.Rect is { Width: > 0, Height: > 0 } &&
                (h.Name == Strings.Back || h.Name == Strings.BackEn),
            cancellationToken).FirstOrDefault();

        if (back is null)
            throw new AgentOperationException(ErrorCodes.ExplorerNavigationTimeout, "Explorer Back button not found.");

        Uia.Activate(back);
        Thread.Sleep(800);
        return Task.CompletedTask;
    }

    public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        EnsureWindow();
        var hit = FindRowHit(file.Name, cancellationToken)
            ?? throw new AgentOperationException(ErrorCodes.ExplorerRowNotFound, $"File row not found: {file.Name}");
        Uia.Activate(hit, doubleClick: true);
        return Task.CompletedTask;
    }

    // ---- internals ----------------------------------------------------------

    private void EnsureWindow()
    {
        if (_hwnd == IntPtr.Zero)
            throw new AgentOperationException(ErrorCodes.ExplorerNotFound, "Start Explorer window has not been selected.");
    }

    private List<ExplorerItem> ReadRows(IntPtr hwnd, string logicalFolder, CancellationToken ct)
    {
        var root = Uia.FromHandle(hwnd);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<ExplorerItem>();

        var hits = Uia.Collect(root, h =>
            (h.ControlType == "ControlType.DataItem" || h.ControlType == "ControlType.ListItem")
            && !string.IsNullOrWhiteSpace(h.Name)
            && h.Rect is { Width: > 0, Height: > 0 }, ct);

        foreach (var h in hits)
        {
            var key = h.ControlType + "|" + h.Name;
            if (!seen.Add(key)) continue;

            var isFolder = ClassifyFolder(h);
            var kind = isFolder ? DocumentKind.Other : DocumentClassifier.ClassifyFile(h.Name);
            var logical = string.IsNullOrEmpty(logicalFolder) ? h.Name : logicalFolder + "\\" + h.Name;
            items.Add(new ExplorerItem(h.Name, logical, isFolder, kind));
        }
        return items;
    }

    private static bool ClassifyFolder(UiaHit h)
    {
        if (DocumentClassifier.IsSupportedOffice(h.Name)) return false;      // .pptx/.xlsx/.xls are files
        if (DocumentClassifier.LooksLikeFolder(h.Name)) return true;         // no extension => folder
        // Dotted name: confirm via the Explorer "type" column / help text.
        try
        {
            var descendants = Uia.Collect(h.Element, _ => true, CancellationToken.None, attempts: 2);
            if (descendants.Any(d => d.Name.Contains(Strings.FileFolderType, StringComparison.Ordinal)))
                return true;
        }
        catch { /* treat as non-folder on read failure */ }
        return false;
    }

    private UiaHit? FindRowHit(string name, CancellationToken ct)
    {
        var root = Uia.FromHandle(_hwnd);
        return Uia.Collect(root, h =>
            (h.ControlType == "ControlType.DataItem" || h.ControlType == "ControlType.ListItem")
            && h.Name == name
            && h.Rect is { Width: > 0, Height: > 0 }, ct).FirstOrDefault();
    }
}
