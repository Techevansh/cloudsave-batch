namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>Non-persistent state store (tests, dry runs).</summary>
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
}
