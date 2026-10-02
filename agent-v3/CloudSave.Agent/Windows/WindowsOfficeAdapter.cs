using System.Windows.Automation;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

/// <summary>
/// Finds the Office window for an opened file and closes it without modifying the source.
/// PowerPoint = PPTFrameClass, Excel = XLMAIN (ports single-office-open + the close logic).
/// </summary>
internal sealed class WindowsOfficeAdapter : IOfficeAdapter
{
    private const string PowerPointClass = "PPTFrameClass";
    private const string ExcelClass = "XLMAIN";

    private readonly IEventWriter _events;
    private readonly WindowsOptions _options;

    public WindowsOfficeAdapter(IEventWriter events, WindowsOptions options)
    {
        _events = events;
        _options = options;
    }

    public Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        var expectedClass = file.Kind == DocumentKind.Excel ? ExcelClass : PowerPointClass;
        var baseName = StripExtension(file.Name);
        var deadline = DateTime.UtcNow + _options.OfficeOpenTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var w in NativeMethods.GetVisibleWindows())
            {
                if (!string.Equals(w.ClassName, expectedClass, StringComparison.Ordinal)) continue;
                if (w.Title.Contains(baseName, StringComparison.Ordinal))
                    return Task.FromResult(new OfficeSession(w.Handle, w.Title, file.Kind));
            }
            Thread.Sleep(300);
        }

        throw new AgentOperationException(ErrorCodes.OfficeOpenTimeout,
            $"Office window did not appear for {file.Name} within {_options.OfficeOpenTimeout.TotalSeconds:0}s.");
    }

    public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        try
        {
            var root = Uia.FromHandle(session.WindowHandle);
            if (root.TryGetCurrentPattern(WindowPattern.Pattern, out var p))
                ((WindowPattern)p).Close();
        }
        catch (Exception ex)
        {
            // Closing is best-effort; report but never force-kill.
            _events.WriteAsync(AgentEvent.Warn("office.close", ErrorCodes.OfficeCloseTimeout, $"Close failed: {ex.Message}"),
                CancellationToken.None).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        var deadline = DateTime.UtcNow + _options.CloseTimeout;
        while (DateTime.UtcNow < deadline)
        {
            // A save/discard prompt can block closing; we never modified the file, so choose discard.
            TryDismissSavePrompt();
            if (!WindowExists(session.WindowHandle)) return Task.CompletedTask;
            Thread.Sleep(300);
        }
        return Task.CompletedTask;
    }

    private void TryDismissSavePrompt()
    {
        try
        {
            // Match the "Don't Save" button robustly across Office builds, while never
            // matching the plain "저장"/"Save" or "다른 이름으로 저장"/"Save As" buttons:
            //   "저장 안 함", "저장 안함"  -> both contain "저장 안"
            //   "Don't Save"               -> case-insensitive contains
            var hit = Uia.Collect(Uia.Root, h =>
                    h.ControlType == "ControlType.Button" && !h.IsOffscreen &&
                    (h.Name.Contains("저장 안", StringComparison.Ordinal)
                     || h.Name.Contains("Don't Save", StringComparison.OrdinalIgnoreCase)
                     || h.Name.Contains("Don’t Save", StringComparison.OrdinalIgnoreCase)),
                CancellationToken.None, attempts: 1).FirstOrDefault();
            if (hit is not null)
            {
                Uia.Activate(hit);
                Thread.Sleep(400);
            }
        }
        catch { /* best effort */ }
    }

    private static bool WindowExists(IntPtr handle) =>
        NativeMethods.GetVisibleWindows().Any(w => w.Handle == handle);

    private static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
