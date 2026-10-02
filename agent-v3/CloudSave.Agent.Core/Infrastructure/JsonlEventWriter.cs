using System.Text.Json;

namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>Writes each <see cref="AgentEvent"/> as one JSON line (JSONL) to a TextWriter (usually stdout for the GUI).</summary>
public sealed class JsonlEventWriter : IEventWriter
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public JsonlEventWriter(TextWriter writer)
    {
        _writer = writer;
    }

    public async Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(evt, _options);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
