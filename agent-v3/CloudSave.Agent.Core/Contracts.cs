namespace CloudSave.Agent.Core;

/// <summary>
/// Reads and navigates a File Explorer window through UI Automation only.
/// Implementations MUST NOT touch the filesystem on U: (no Get-ChildItem / File APIs),
/// MUST re-discover elements after navigation (UIA elements go stale), and MUST NOT
/// use SendKeys, Ctrl+A/F6, or guessed coordinates.
/// </summary>
public interface IExplorerAdapter
{
    /// <summary>
    /// Pick the start Explorer window the user already opened and return its logical identity
    /// (the address-bar path when readable, else the window title). Every LogicalPath of the
    /// run is rooted here, so it also namespaces the resume records.
    /// </summary>
    Task<string> GetStartContextAsync(CancellationToken cancellationToken);

    /// <summary>Read the current rows of the active folder.</summary>
    Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken);

    /// <summary>
    /// Navigate into a folder. Returns only once Explorer has verifiably left the current folder;
    /// throws (and stays put) when the navigation did not happen.
    /// </summary>
    Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken);

    /// <summary>
    /// Return to the folder that was current before the matching <see cref="EnterFolderAsync"/>.
    /// Returns only once Explorer is verifiably back there; otherwise throws
    /// <see cref="ErrorCodes.ExplorerLocationLost"/> and the run must stop.
    /// </summary>
    Task GoBackAsync(CancellationToken cancellationToken);

    /// <summary>Open a file by double-activating its Explorer row (real BoundingRectangle centre, verified hit-test).</summary>
    Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken);
}

/// <summary>Finds the Office window for an opened file and closes it without modifying the source.</summary>
public interface IOfficeAdapter
{
    /// <summary>
    /// Called immediately before the file is opened. Records the Office windows that already
    /// exist so the agent only ever drives (and closes) the window its own open produced,
    /// never a document the user already had open.
    /// </summary>
    Task PrepareForOpenAsync(ExplorerItem file, CancellationToken cancellationToken);

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

    /// <summary>Remove every record at or under <paramref name="logicalRoot"/>. Returns how many were removed.</summary>
    Task<int> RemoveUnderAsync(string logicalRoot, CancellationToken cancellationToken);
}

public interface IEventWriter
{
    Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken);
}
