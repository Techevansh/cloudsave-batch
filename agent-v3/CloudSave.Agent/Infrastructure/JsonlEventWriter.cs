using System.Text.Json;
using CloudSave.Agent.Core;

namespace CloudSave.Agent.Infrastructure;

public sealed class JsonlEventWriter : IEventWriter
{
    private readonly TextWriter _writer;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public JsonlEventWriter(TextWriter writer)
    {
        _writer = writer;
    }

    public async Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(evt, _options);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _writer.FlushAsync();
    }
}
