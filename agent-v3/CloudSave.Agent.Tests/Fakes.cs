using System.Collections.Concurrent;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Tests;

internal sealed class FakeEvents : IEventWriter
{
    public readonly List<AgentEvent> Events = new();

    public Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        lock (Events) Events.Add(evt);
        return Task.CompletedTask;
    }

    public IEnumerable<string> Types => Events.Select(e => e.Type);
    public bool Has(string type) => Events.Any(e => e.Type == type);
    public bool HasCode(string code) => Events.Any(e => e.Code == code);
    public int Count(string type) => Events.Count(e => e.Type == type);
}

internal sealed class FakeStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, ProcessingRecord> _records = new(StringComparer.Ordinal);
    public readonly List<ProcessingRecord> Upserts = new();

    public Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken)
    {
        _records.TryGetValue(logicalPath, out var r);
        return Task.FromResult<ProcessingRecord?>(r);
    }

    public Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        _records[record.LogicalPath] = record;
        lock (Upserts) Upserts.Add(record);
        return Task.CompletedTask;
    }

    public ProcessingRecord? Current(string key)
    {
        _records.TryGetValue(key, out var r);
        return r;
    }
}

internal sealed class FakeExplorer : IExplorerAdapter
{
    public string StartContext = "";
    public readonly Dictionary<string, List<ExplorerItem>> Rows = new(StringComparer.Ordinal);
    public readonly List<string> Activated = new();
    public readonly List<string> Entered = new();
    public int BackCount;

    /// <summary>Optional: return an exception to throw for a given (folder, zero-based call index); null = succeed.</summary>
    public Func<string, int, Exception?>? ReadFailure;
    private readonly Dictionary<string, int> _readCalls = new(StringComparer.Ordinal);

    public Func<ExplorerItem, Exception?>? EnterFailure;

    public static ExplorerItem File(string name, string? logicalPath = null) =>
        new(name, logicalPath ?? name, false, DocumentClassifier.ClassifyFile(name));

    public static ExplorerItem Folder(string name, string? logicalPath = null) =>
        new(name, logicalPath ?? name, true, DocumentKind.Other);

    public Task<string> GetStartContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(StartContext);

    public Task<IReadOnlyList<ExplorerItem>> ReadItemsAsync(string logicalFolder, CancellationToken cancellationToken)
    {
        var idx = _readCalls.TryGetValue(logicalFolder, out var c) ? c : 0;
        _readCalls[logicalFolder] = idx + 1;

        var ex = ReadFailure?.Invoke(logicalFolder, idx);
        if (ex is not null) throw ex;

        var list = Rows.TryGetValue(logicalFolder, out var rows) ? rows : new List<ExplorerItem>();
        return Task.FromResult<IReadOnlyList<ExplorerItem>>(list);
    }

    public Task EnterFolderAsync(ExplorerItem folder, CancellationToken cancellationToken)
    {
        var ex = EnterFailure?.Invoke(folder);
        if (ex is not null) throw ex;
        Entered.Add(folder.LogicalPath);
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CancellationToken cancellationToken)
    {
        BackCount++;
        return Task.CompletedTask;
    }

    public Task ActivateFileAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        Activated.Add(file.LogicalPath);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOffice : IOfficeAdapter
{
    public int CloseCount;
    public Func<ExplorerItem, Exception?>? WaitFailure;

    public Task<OfficeSession> WaitForDocumentAsync(ExplorerItem file, CancellationToken cancellationToken)
    {
        var ex = WaitFailure?.Invoke(file);
        if (ex is not null) throw ex;
        var kind = file.Kind == DocumentKind.Excel ? DocumentKind.Excel : DocumentKind.PowerPoint;
        return Task.FromResult(new OfficeSession(1, file.Name, kind));
    }

    public Task CloseWithoutSavingAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        CloseCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeAnalyzer : IAnalyzerAdapter
{
    public bool InteractiveAuth;
    public Func<OfficeSession, Exception?>? EnsureFailure;
    public Func<OfficeSession, AnalysisResult>? RunOverride;
    public Action<OfficeSession>? OnRun;

    public Task EnsureAnalyzerOpenAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        var ex = EnsureFailure?.Invoke(session);
        if (ex is not null) throw ex;
        return Task.CompletedTask;
    }

    public Task<AnalysisResult> RunAnalysisAsync(OfficeSession session, CancellationToken cancellationToken)
    {
        OnRun?.Invoke(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (RunOverride is not null) return Task.FromResult(RunOverride(session));
        return Task.FromResult(AnalysisResult.Completed(InteractiveAuth));
    }
}
