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
);

public sealed record ProcessingRecord(
    string LogicalPath,
    string Status,
    int Attempts,
    DateTimeOffset UpdatedAt,
    string? LastError = null
);
