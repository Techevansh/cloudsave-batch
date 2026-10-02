namespace CloudSave.Agent.Core;

/// <summary>
/// Reads and navigates a File Explorer window through UI Automation only.
/// Implementations MUST NOT touch the filesystem on U: (no Get-ChildItem / File APIs),
/// MUST re-discover elements after navigation (UIA elements go stale), and MUST NOT
/// use SendKeys, Ctrl+A/F6, or guessed coordinates.
/// </summary>
public interface IExplorerAdapter
{
    /// <summary>Pick the start Explorer window the user already opened and return its logical identity.</summary>
    Task<string> GetStartContextAsync(CancellationToken cancellationToken);

    /// <summary>Read the current rows of the active folder.</summary>
    Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken);

    Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken);

    Task GoBackAsync(CancellationToken cancellationToken);

    /// <summary>Open a file by invoking its Explorer row (InvokePattern, else its real BoundingRectangle centre).</summary>
    Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken);
}

/// <summary>Finds the Office window for an opened file and closes it without modifying the source.</summary>
public interface IOfficeAdapter
{
    Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken);

    /// <summary>Close via WindowPattern.Close; if a save prompt appears, choose discard (the agent never edits the file).</summary>
    Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken);
}

/// <summary>
/// Opens the existing add-in (PPTX analyzer / Excel analyzer), locates the task-pane
/// start button, runs the analysis, and reports cached-vs-interactive auth outcome.
/// </summary>
public interface IAnalyzerAdapter
{
    Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken);

    Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken);
}

public interface IStateStore
{
    Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken);
    Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken);
}

public interface IEventWriter
{
    Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken);
}
