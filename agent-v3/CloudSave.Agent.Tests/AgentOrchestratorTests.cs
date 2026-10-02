using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class AgentOrchestratorTests
{
    [Fact]
    public async Task SkipsPreviouslyCompletedFile()
    {
        var explorer = new FakeExplorer(new[]
        {
            new ExplorerItem("done.pptx", "done.pptx", false, DocumentKind.PowerPoint)
        });

        var state = new FakeStateStore();
        await state.UpsertAsync(new ProcessingRecord("done.pptx", "completed", 1, DateTimeOffset.UtcNow), CancellationToken.None);

        var orchestrator = new AgentOrchestrator(
            new FakeEvents(),
            explorer,
            new FakeOffice(),
            new FakeAnalyzer(),
            state);

        var result = await orchestrator.RunAsync(new AgentRunRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.Processed);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, explorer.Activated);
    }

    [Fact]
    public async Task ContinuesAfterOneFileFails()
    {
        var explorer = new FakeExplorer(new[]
        {
            new ExplorerItem("bad.pptx", "bad.pptx", false, DocumentKind.PowerPoint),
            new ExplorerItem("good.pptx", "good.pptx", false, DocumentKind.PowerPoint)
        });

        var analyzer = new FakeAnalyzer { FailFirst = true };

        var orchestrator = new AgentOrchestrator(
            new FakeEvents(),
            explorer,
            new FakeOffice(),
            analyzer,
            new FakeStateStore());

        var result = await orchestrator.RunAsync(new AgentRunRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Failed);
    }

    private sealed class FakeExplorer : IExplorerAdapter
    {
        private readonly IReadOnlyList<ExplorerItem> _items;
        public int Activated;

        public FakeExplorer(IReadOnlyList<ExplorerItem> items) => _items = items;
        public Task<string> GetStartContextAsync(CancellationToken cancellationToken) => Task.FromResult("root");
        public Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken) => Task.FromResult(_items);
        public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken) { Activated++; return Task.CompletedTask; }
    }

    private sealed class FakeOffice : IOfficeAdapter
    {
        public Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken) =>
            Task.FromResult(new OfficeSession(1, file.Name, file.Kind));

        public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAnalyzer : IAnalyzerAdapter
    {
        public bool FailFirst;
        private int _calls;

        public Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken)
        {
            _calls++;
            if (FailFirst && _calls == 1)
                return Task.FromResult(new AnalysisResult(false, false, "TEST_FAIL", "expected failure"));

            return Task.FromResult(new AnalysisResult(true, false));
        }
    }

    private sealed class FakeStateStore : IStateStore
    {
        private readonly Dictionary<string, ProcessingRecord> _map = new();
        public Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken) { _map.TryGetValue(logicalPath, out var r); return Task.FromResult(r); }
        public Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken) { _map[record.LogicalPath] = record; return Task.CompletedTask; }
    }

    private sealed class FakeEvents : IEventWriter
    {
        public Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
