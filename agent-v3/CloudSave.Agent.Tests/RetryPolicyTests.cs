using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class RetryPolicyTests
{
    private static RetryPolicy Immediate() => RetryPolicy.Immediate();

    [Fact]
    public async Task Succeeds_on_first_try_without_retry()
    {
        var events = new FakeEvents();
        var calls = 0;
        var result = await Immediate().ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(42);
        }, "op", events, default);

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
        Assert.False(events.Has("retry.transient"));
    }

    [Fact]
    public async Task Retries_transient_then_succeeds()
    {
        var events = new FakeEvents();
        var calls = 0;
        var result = await Immediate().ExecuteAsync(_ =>
        {
            calls++;
            if (calls < 3) throw new AgentOperationException(ErrorCodes.UiaTransient, "flaky");
            return Task.FromResult("ok");
        }, "op", events, default);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
        Assert.Equal(2, events.Count("retry.transient"));
    }

    [Fact]
    public async Task Non_transient_is_not_retried()
    {
        var events = new FakeEvents();
        var calls = 0;
        await Assert.ThrowsAsync<AgentOperationException>(async () =>
            await Immediate().ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new AgentOperationException(ErrorCodes.AnalysisTimeout, "fatal");
            }, "op", events, default));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Exhausts_backoff_then_rethrows_last()
    {
        var events = new FakeEvents();
        var calls = 0;
        // Two retry slots => three total attempts.
        var policy = RetryPolicy.Immediate(new[] { TimeSpan.Zero, TimeSpan.Zero });
        var ex = await Assert.ThrowsAsync<AgentOperationException>(async () =>
            await policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new AgentOperationException(ErrorCodes.TaskpaneUiaTransient, "always");
            }, "op", events, default));

        Assert.Equal(3, calls);
        Assert.Equal(ErrorCodes.TaskpaneUiaTransient, ex.Code);
    }

    [Fact]
    public async Task Pre_cancelled_token_does_not_invoke_operation()
    {
        var events = new FakeEvents();
        var calls = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Immediate().ExecuteAsync(_ =>
            {
                calls++;
                return Task.FromResult(1);
            }, "op", events, cts.Token));

        Assert.Equal(0, calls);
    }
}
