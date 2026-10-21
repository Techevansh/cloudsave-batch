namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>Non-persistent state store (tests, dry runs, and runs with resume turned off).</summary>
public sealed class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, ProcessingRecord> _records = new(StringComparer.Ordinal);

    public Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken)
    {
        _records.TryGetValue(logicalPath, out var record);
        return Task.FromResult<ProcessingRecord?>(record);
    }

    public Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        _records[record.LogicalPath] = record;
        return Task.CompletedTask;
    }

    public Task<int> RemoveUnderAsync(string logicalRoot, CancellationToken cancellationToken)
    {
        var keys = _records.Keys.Where(k => LogicalPaths.IsAtOrUnder(k, logicalRoot)).ToList();
        foreach (var k in keys) _records.Remove(k);
        return Task.FromResult(keys.Count);
    }
}
