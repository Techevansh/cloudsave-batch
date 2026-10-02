namespace CloudSave.Agent.Core;

public interface IExplorerAdapter
{
    Task<string> GetStartContextAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken);
    Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken);
    Task GoBackAsync(CancellationToken cancellationToken);
    Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken);
}

public interface IOfficeAdapter
{
    Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken);
    Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken);
}

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
