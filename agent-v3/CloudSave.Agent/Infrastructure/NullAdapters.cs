using CloudSave.Agent.Core;

namespace CloudSave.Agent.Infrastructure;

public sealed class NullExplorerAdapter : IExplorerAdapter
{
    public Task<string> GetStartContextAsync(CancellationToken cancellationToken) =>
        throw new AgentOperationException("EXPLORER_NOT_IMPLEMENTED", "ExplorerAdapter is not implemented yet.");

    public Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken) =>
        throw new AgentOperationException("EXPLORER_NOT_IMPLEMENTED", "ExplorerAdapter is not implemented yet.");

    public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken) =>
        throw new AgentOperationException("EXPLORER_NOT_IMPLEMENTED", "ExplorerAdapter is not implemented yet.");

    public Task GoBackAsync(CancellationToken cancellationToken) =>
        throw new AgentOperationException("EXPLORER_NOT_IMPLEMENTED", "ExplorerAdapter is not implemented yet.");

    public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken) =>
        throw new AgentOperationException("EXPLORER_NOT_IMPLEMENTED", "ExplorerAdapter is not implemented yet.");
}

public sealed class NullOfficeAdapter : IOfficeAdapter
{
    public Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken) =>
        throw new AgentOperationException("OFFICE_NOT_IMPLEMENTED", "OfficeAdapter is not implemented yet.");

    public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class NullAnalyzerAdapter : IAnalyzerAdapter
{
    public Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken) =>
        throw new AgentOperationException("ANALYZER_NOT_IMPLEMENTED", "AnalyzerAdapter is not implemented yet.");

    public Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken) =>
        Task.FromResult(new AnalysisResult(false, false, "ANALYZER_NOT_IMPLEMENTED", "AnalyzerAdapter is not implemented yet."));
}
