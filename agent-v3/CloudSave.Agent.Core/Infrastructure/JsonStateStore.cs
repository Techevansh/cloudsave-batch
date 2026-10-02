using System.Text.Json;

namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>
/// Persistent resume store backed by a single JSON file keyed by logical path.
/// JSON (not SQLite) keeps the self-contained publish free of native dependencies.
/// Loads tolerantly (missing/empty/corrupt file => empty) and writes atomically
/// (temp file + replace) so an interrupted write cannot corrupt the store.
/// </summary>
public sealed class JsonStateStore : IStateStore
{
    private readonly string _path;
    private readonly Dictionary<string, ProcessingRecord> _records;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private JsonStateStore(string path, Dictionary<string, ProcessingRecord> records)
    {
        _path = path;
        _records = records;
    }

    public static JsonStateStore Load(string path)
    {
        var records = new Dictionary<string, ProcessingRecord>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, ProcessingRecord>>(raw, Options);
                    if (loaded is not null)
                        foreach (var kv in loaded)
                            records[kv.Key] = kv.Value;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable state must never block a run; start from an empty store.
            records.Clear();
        }
        return new JsonStateStore(path, records);
    }

    public Task<ProcessingRecord?> GetAsync(string logicalPath, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            _records.TryGetValue(logicalPath, out var record);
            return Task.FromResult<ProcessingRecord?>(record);
        }
    }

    public async Task UpsertAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_records)
            {
                _records[record.LogicalPath] = record;
            }
            await PersistAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync()
    {
        Dictionary<string, ProcessingRecord> snapshot;
        lock (_records)
        {
            snapshot = new Dictionary<string, ProcessingRecord>(_records, StringComparer.Ordinal);
        }

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(snapshot, Options);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);

        // Atomic-ish replace.
        if (File.Exists(_path))
            File.Replace(tmp, _path, null);
        else
            File.Move(tmp, _path);
    }
}
