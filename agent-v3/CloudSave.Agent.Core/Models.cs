namespace CloudSave.Agent.Core;

public enum StartMode
{
    CurrentVisibleExplorerFolder
}

public enum DocumentKind
{
    PowerPoint,
    Excel,
    Other
}

/// <summary>High-level stage of the per-run / per-file state machine (for events/telemetry).</summary>
public enum AgentStage
{
    DiscoverStartExplorer,
    ReadExplorer,
    OpenDocument,
    WaitForOffice,
    EnsureAnalyzer,
    StartAnalysis,
    WaitForAuthentication,
    WaitForAnalysis,
    CloseOffice,
    PersistResult,
    TraverseFolder,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Analysis task-pane sub-state (owned by the AnalyzerAdapter, surfaced in results).</summary>
public enum AnalysisState
{
    Idle,
    StartRequested,
    Working,
    InteractiveAuth,
    Completed,
    Failed,
    TimedOut
}

public sealed class AgentRunRequest
{
    public StartMode StartMode { get; init; } = StartMode.CurrentVisibleExplorerFolder;

    public int MaxDepth { get; init; } = 10;

    /// <summary>When false, only the start folder is processed (no subfolder recursion). Phase-1/2/3 testing.</summary>
    public bool EnableRecursion { get; init; } = true;

    /// <summary>Cap on Office files processed this run. 0 = unlimited. Set to 1 for a single-file milestone test.</summary>
    public int MaxOfficeFiles { get; init; } = 0;

    /// <summary>
    /// When true, files already recorded as completed in the state store are skipped (resume).
    /// A run that finishes with no failures clears its folder's records, so resume only ever
    /// continues an interrupted or partly failed run; the next full run starts fresh.
    /// </summary>
    public bool EnableResume { get; init; } = true;

    /// <summary>Pause after the start folder is identified (shown to the user) before any file is opened. F12 cancels.</summary>
    public TimeSpan StartDelay { get; init; } = TimeSpan.Zero;
}

/// <summary>One file (or folder subtree) that did not complete, for the end-of-run report.</summary>
public sealed record FileFailure(string LogicalPath, string Code, string Message);

public sealed class AgentRunResult
{
    public bool Success { get; init; }
    public int Processed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    /// <summary>Completed files whose per-destination upload lines could not be read (see UPLOAD_UNVERIFIED).</summary>
    public int Unverified { get; init; }
    public string Root { get; init; } = "";
    public IReadOnlyList<FileFailure> Failures { get; init; } = Array.Empty<FileFailure>();
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static AgentRunResult Ok(int processed, int failed, int skipped) =>
        new() { Success = true, Processed = processed, Failed = failed, Skipped = skipped };

    public static AgentRunResult Fail(string code, string message, int processed = 0, int failed = 0, int skipped = 0) =>
        new() { Success = false, ErrorCode = code, Message = message, Processed = processed, Failed = failed, Skipped = skipped };
}

/// <summary>Data of the agent.start event (what this run will do), rendered as the console banner.</summary>
public sealed record AgentStartInfo(bool Recursion, int MaxOfficeFiles, bool Resume, string? LogFile);

/// <summary>The end-of-run report, carried as the Data of the final agent.complete / agent.failed event.</summary>
public sealed record RunSummary(
    string Root,
    int Processed,
    int Failed,
    int Skipped,
    int Unverified,
    IReadOnlyList<FileFailure> Failures,
    string? LogFile);

/// <summary>
/// A single row read from a File Explorer window through UI Automation.
/// LogicalPath is a UIA-derived identity (folderChain + name), never a real U: filesystem path,
/// because U: cannot be enumerated through filesystem APIs.
/// </summary>
public sealed record ExplorerItem(
    string Name,
    string LogicalPath,
    bool IsFolder,
    DocumentKind Kind
);

public sealed record OfficeSession(
    nint WindowHandle,
    string Title,
    DocumentKind Kind
);

public sealed record AnalysisResult(
    bool Success,
    bool AuthenticationWasInteractive,
    string? ErrorCode = null,
    string? Message = null,
    bool Verified = true
)
{
    /// <param name="verified">False when the add-in finished but its per-destination "✅" lines could not be read.</param>
    public static AnalysisResult Completed(bool interactiveAuth, bool verified = true) =>
        new(true, interactiveAuth, null, null, verified);

    public static AnalysisResult Failure(string code, string message) => new(false, false, code, message);
}

public sealed record ProcessingRecord(
    string LogicalPath,
    string Status,
    int Attempts,
    DateTimeOffset UpdatedAt,
    string? LastError = null
);

/// <summary>LogicalPath composition: parent + "\" + name, rooted at the start context.</summary>
public static class LogicalPaths
{
    public static string Combine(string folder, string name) =>
        string.IsNullOrEmpty(folder) ? name : folder + "\\" + name;

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> itself or lies beneath it.</summary>
    public static bool IsAtOrUnder(string path, string root) =>
        string.IsNullOrEmpty(root)
        || string.Equals(path, root, StringComparison.Ordinal)
        || path.StartsWith(root + "\\", StringComparison.Ordinal);
}

/// <summary>Canonical status strings stored in the state store.</summary>
public static class ProcessingStatus
{
    public const string Opening = "opening";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
