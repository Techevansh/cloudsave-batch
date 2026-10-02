using CloudSave.Agent.Core;
using CloudSave.Agent.Core.Infrastructure;
using CloudSave.Agent.Windows;

// Entry point. Streams JSONL events to stdout for the Electron GUI.
// Phased-testing knobs come from environment variables so the GUI / launcher can
// set them without rebuilding:
//   CLOUDSAVE_RECURSION      = true|false   (default true)
//   CLOUDSAVE_MAX_FILES      = int          (default 0 = unlimited; set 1 for a single-file milestone)
//   CLOUDSAVE_RESUME         = true|false   (default true)
//   CLOUDSAVE_MAX_DEPTH      = int          (default 10)
//   CLOUDSAVE_STATE_FILE     = path         (default .\logs\state.json)

var log = new JsonlEventWriter(Console.Out);
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var request = new AgentRunRequest
{
    StartMode = StartMode.CurrentVisibleExplorerFolder,
    MaxDepth = EnvInt("CLOUDSAVE_MAX_DEPTH", 10),
    EnableRecursion = EnvBool("CLOUDSAVE_RECURSION", true),
    MaxOfficeFiles = EnvInt("CLOUDSAVE_MAX_FILES", 0),
    EnableResume = EnvBool("CLOUDSAVE_RESUME", true),
};

var stateFile = Environment.GetEnvironmentVariable("CLOUDSAVE_STATE_FILE");
if (string.IsNullOrWhiteSpace(stateFile))
    stateFile = Path.Combine(AppContext.BaseDirectory, "logs", "state.json");

await log.WriteAsync(AgentEvent.Info("agent.start",
    $"CloudSave Agent v3 starting (recursion={request.EnableRecursion}, maxFiles={request.MaxOfficeFiles}, resume={request.EnableResume})"),
    cts.Token);

IStateStore state = JsonStateStore.Load(stateFile);

var orchestrator = new AgentOrchestrator(
    log,
    new NotImplementedExplorerAdapter(),
    new NotImplementedOfficeAdapter(),
    new NotImplementedAnalyzerAdapter(),
    state);

var result = await orchestrator.RunAsync(request, cts.Token);

await log.WriteAsync(
    result.Success
        ? AgentEvent.Info("agent.complete",
            $"Processed={result.Processed}; Failed={result.Failed}; Skipped={result.Skipped}")
        : AgentEvent.Error("agent.failed", result.ErrorCode ?? "UNKNOWN", result.Message ?? "Agent failed",
            new { result.Processed, result.Failed, result.Skipped }),
    CancellationToken.None);

Environment.ExitCode = result.Success ? 0 : 1;

static bool EnvBool(string name, bool fallback)
{
    var v = Environment.GetEnvironmentVariable(name);
    return bool.TryParse(v, out var b) ? b : fallback;
}

static int EnvInt(string name, int fallback)
{
    var v = Environment.GetEnvironmentVariable(name);
    return int.TryParse(v, out var n) ? n : fallback;
}
