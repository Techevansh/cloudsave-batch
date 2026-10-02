using CloudSave.Agent.Core;
using CloudSave.Agent.Infrastructure;

var log = new JsonlEventWriter(Console.Out);
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await log.WriteAsync(AgentEvent.Info("agent.start", "CloudSave Agent v3 starting"), cts.Token);

var orchestrator = new AgentOrchestrator(
    log,
    new NullExplorerAdapter(),
    new NullOfficeAdapter(),
    new NullAnalyzerAdapter(),
    new InMemoryStateStore()
);

var result = await orchestrator.RunAsync(new AgentRunRequest
{
    StartMode = StartMode.CurrentVisibleExplorerFolder,
    MaxDepth = 10
}, cts.Token);

await log.WriteAsync(
    result.Success
        ? AgentEvent.Info("agent.complete", $"Processed={result.Processed}; Failed={result.Failed}; Skipped={result.Skipped}")
        : AgentEvent.Error("agent.failed", result.ErrorCode ?? "UNKNOWN", result.Message ?? "Agent failed"),
    CancellationToken.None
);

Environment.ExitCode = result.Success ? 0 : 1;
