using CloudSave.Agent.Core;

namespace CloudSave.Agent.Infrastructure;

public sealed class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, ProcessingRecord> _records = new(StringComparer.Ordinal);

    public Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken)
    {
        _records.TryGetValue(logicalPath, out var record);
        return Task.FromResult(record);
    }

    public Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        _records[record.LogicalPath] = record;
        return Task.CompletedTask;
    }
}
