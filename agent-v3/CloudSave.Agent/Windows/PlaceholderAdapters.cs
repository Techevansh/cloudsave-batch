using CloudSave.Agent.Core;

namespace CloudSave.Agent.Windows;

// =============================================================================
// Stage A placeholders.
//
// These keep the exe compiling and the wiring honest until the real UI
// Automation adapters land in Stage B/C. Each throws a CATEGORIZED error so the
// orchestrator and GUI already behave correctly against "not implemented yet",
// rather than crashing with a raw exception.
//
// Stage B replaces WindowsExplorerAdapter; Stage C replaces the Office and
// Analyzer adapters. See the implementation notes on each real adapter type.
// =============================================================================

public sealed class NotImplementedExplorerAdapter : IExplorerAdapter
{
    private static AgentOperationException NotReady() =>
        new(ErrorCodes.ExplorerNotFound, "ExplorerAdapter is not implemented yet (Stage B).");

    public Task<string> GetStartContextAsync(CancellationToken cancellationToken) => throw NotReady();
    public Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken) => throw NotReady();
    public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken) => throw NotReady();
    public Task GoBackAsync(CancellationToken cancellationToken) => throw NotReady();
    public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken) => throw NotReady();
}

public sealed class NotImplementedOfficeAdapter : IOfficeAdapter
{
    public Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken) =>
        throw new AgentOperationException(ErrorCodes.OfficeOpenTimeout, "OfficeAdapter is not implemented yet (Stage C).");

    public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class NotImplementedAnalyzerAdapter : IAnalyzerAdapter
{
    public Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken) =>
        throw new AgentOperationException(ErrorCodes.AnalyzerButtonNotFound, "AnalyzerAdapter is not implemented yet (Stage C).");

    public Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken) =>
        Task.FromResult(AnalysisResult.Failure(ErrorCodes.AnalysisFailed, "AnalyzerAdapter is not implemented yet (Stage C)."));
}
