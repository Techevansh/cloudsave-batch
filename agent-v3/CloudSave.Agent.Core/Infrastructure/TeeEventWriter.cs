namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>
/// Fans every event out to several writers (e.g. the console and the JSONL log file).
/// A failing writer (disk full, closed pipe) never breaks the run or the other writers.
/// </summary>
public sealed class TeeEventWriter : IEventWriter
{
    private readonly IReadOnlyList<IEventWriter> _writers;

    public TeeEventWriter(params IEventWriter[] writers)
    {
        _writers = writers;
    }

    public async Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        foreach (var w in _writers)
        {
            try
            {
                await w.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Logging must never take the agent down.
            }
        }
    }
}
