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

    /// <summary>When true, files already recorded as completed in the state store are skipped (resume).</summary>
    public bool EnableResume { get; init; } = true;
}

public sealed class AgentRunResult
{
    public bool Success { get; init; }
    public int Processed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static AgentRunResult Ok(int processed, int failed, int skipped) =>
        new() { Success = true, Processed = processed, Failed = failed, Skipped = skipped };

    public static AgentRunResult Fail(string code, string message, int processed = 0, int failed = 0, int skipped = 0) =>
        new() { Success = false, ErrorCode = code, Message = message, Processed = processed, Failed = failed, Skipped = skipped };
}

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
    string? Message = null
)
{
    public static AnalysisResult Completed(bool interactiveAuth) => new(true, interactiveAuth);
    public static AnalysisResult Failure(string code, string message) => new(false, false, code, message);
}

public sealed record ProcessingRecord(
    string LogicalPath,
    string Status,
    int Attempts,
    DateTimeOffset UpdatedAt,
    string? LastError = null
);

/// <summary>Canonical status strings stored in the state store.</summary>
public static class ProcessingStatus
{
    public const string Opening = "opening";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
