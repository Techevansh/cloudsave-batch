namespace CloudSave.Agent.Core;

/// <summary>
/// Bounded retry for transient UI Automation conditions (ARCHITECTURE_V3.md "Retry policy").
/// Default backoff: 250 ms, 500 ms, 1 s, 2 s, then the last failure is rethrown.
/// A delay provider is injectable so unit tests run with zero delay.
/// </summary>
public sealed class RetryPolicy
{
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoff = new[]
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    };

    private readonly IReadOnlyList<TimeSpan> _backoff;
    private readonly Func<Exception, bool> _isTransient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RetryPolicy(
        IReadOnlyList<TimeSpan>? backoff = null,
        Func<Exception, bool>? isTransient = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _backoff = backoff ?? DefaultBackoff;
        _isTransient = isTransient ?? DefaultIsTransient;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>A policy whose delays are instantaneous (for tests).</summary>
    public static RetryPolicy Immediate(IReadOnlyList<TimeSpan>? backoff = null, Func<Exception, bool>? isTransient = null) =>
        new(backoff, isTransient, static (_, _) => Task.CompletedTask);

    public static bool DefaultIsTransient(Exception ex) =>
        ex is AgentOperationException { IsTransient: true };

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        IEventWriter events,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _backoff.Count + 1;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (_isTransient(ex) && attempt < maxAttempts)
            {
                var wait = _backoff[attempt - 1];
                var code = (ex as AgentOperationException)?.Code ?? ErrorCodes.UiaTransient;
                await events.WriteAsync(
                    AgentEvent.Warn("retry.transient", code,
                        $"{operationName} transient failure (attempt {attempt}/{maxAttempts}); retrying in {wait.TotalMilliseconds:0} ms: {ex.Message}"),
                    cancellationToken).ConfigureAwait(false);
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        IEventWriter events,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync<bool>(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        }, operationName, events, cancellationToken).ConfigureAwait(false);
    }
}
