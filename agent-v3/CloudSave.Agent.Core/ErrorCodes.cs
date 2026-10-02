namespace CloudSave.Agent.Core;

/// <summary>
/// Canonical error taxonomy (ARCHITECTURE_V3.md). Every failure is categorized with one of
/// these codes instead of a raw exception message, so the GUI and the retry policy can react
/// per category. Adapters throw <see cref="AgentOperationException"/> carrying one of these.
/// </summary>
public static class ErrorCodes
{
    // Explorer
    public const string ExplorerNotFound = "EXPLORER_NOT_FOUND";
    public const string ExplorerRowNotFound = "EXPLORER_ROW_NOT_FOUND";
    public const string ExplorerNavigationTimeout = "EXPLORER_NAVIGATION_TIMEOUT";

    // Office window
    public const string OfficeOpenTimeout = "OFFICE_OPEN_TIMEOUT";
    public const string OfficeWrongWindow = "OFFICE_WRONG_WINDOW";
    public const string OfficeProtectedView = "OFFICE_PROTECTED_VIEW";
    public const string OfficeCloseTimeout = "OFFICE_CLOSE_TIMEOUT";

    // Analyzer / task pane
    public const string AnalyzerButtonNotFound = "ANALYZER_BUTTON_NOT_FOUND";
    public const string AnalyzerTaskpaneTimeout = "ANALYZER_TASKPANE_TIMEOUT";
    public const string TaskpaneUiaTransient = "TASKPANE_UIA_TRANSIENT";
    public const string AnalysisButtonNotFound = "ANALYSIS_BUTTON_NOT_FOUND";

    // Auth / analysis
    public const string AuthWaitTimeout = "AUTH_WAIT_TIMEOUT";
    public const string AnalysisTimeout = "ANALYSIS_TIMEOUT";
    public const string AnalysisFailed = "ANALYSIS_FAILED";

    // UIA generic
    public const string UiaElementStale = "UIA_ELEMENT_STALE";
    public const string UiaTransient = "UIA_TRANSIENT";

    // Control flow
    public const string UserCancelled = "USER_CANCELLED";
    public const string Unexpected = "UNEXPECTED";
    public const string Unhandled = "UNHANDLED";

    private static readonly HashSet<string> TransientCodes = new(StringComparer.Ordinal)
    {
        UiaTransient,
        UiaElementStale,
        TaskpaneUiaTransient,
    };

    /// <summary>True for codes that are worth a bounded retry (transient UIA conditions).</summary>
    public static bool IsTransient(string code) => code is not null && TransientCodes.Contains(code);
}

/// <summary>A categorized failure. <see cref="Code"/> is one of <see cref="ErrorCodes"/>.</summary>
public sealed class AgentOperationException : Exception
{
    public AgentOperationException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }

    public bool IsTransient => ErrorCodes.IsTransient(Code);

    public static AgentOperationException Transient(string code, string message, Exception? inner = null)
    {
        if (!ErrorCodes.IsTransient(code))
            throw new ArgumentException($"Code '{code}' is not a transient code.", nameof(code));
        return new AgentOperationException(code, message, inner);
    }
}
