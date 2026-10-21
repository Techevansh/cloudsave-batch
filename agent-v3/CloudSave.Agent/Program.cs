using System.IO;
using System.Text;
using CloudSave.Agent.Core;
using CloudSave.Agent.Core.Infrastructure;
using CloudSave.Agent.Windows;

// Entry point.
//   GUI mode (default): JSONL events on stdout for the Electron GUI.
//   Console mode (CLOUDSAVE_CONSOLE=human, used by the .bat launchers): Korean progress lines.
// In both modes the full JSONL log is also written to <log dir>/run-yyyyMMdd-HHmmss.jsonl.
//
// Knobs (environment variables, so the launcher / GUI can set them without rebuilding):
//   CLOUDSAVE_RECURSION        = true|false   (default true)
//   CLOUDSAVE_MAX_FILES        = int          (default 0 = unlimited; 1 for a single-file test)
//   CLOUDSAVE_RESUME           = true|false   (default true; false = remember nothing)
//   CLOUDSAVE_MAX_DEPTH        = int          (default 10)
//   CLOUDSAVE_START_DELAY_SEC  = int          (default 0; pause after showing the target folder)
//   CLOUDSAVE_CONSOLE          = human|jsonl  (default jsonl)
//   CLOUDSAVE_LOG_DIR          = path         (default <exe>/logs)
//   CLOUDSAVE_STATE_FILE       = path         (default <exe>/logs/state.json)
//   CLOUDSAVE_*_SEC            = per-stage timeouts (see WindowsOptions)
// Emergency stop: press F12 at any time.

// Must precede every window / UIA call: UIA rectangles and mouse coordinates must share one pixel space.
NativeMethods.EnableDpiAwareness();
try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
catch { /* no console attached (GUI host): stdout encoding is the host's concern */ }

var human = string.Equals(Environment.GetEnvironmentVariable("CLOUDSAVE_CONSOLE"), "human", StringComparison.OrdinalIgnoreCase);

var logDir = Environment.GetEnvironmentVariable("CLOUDSAVE_LOG_DIR");
if (string.IsNullOrWhiteSpace(logDir))
    logDir = Path.Combine(AppContext.BaseDirectory, "logs");

string? logFile = null;
StreamWriter? logStream = null;
try
{
    Directory.CreateDirectory(logDir);
    logFile = Path.Combine(logDir, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
    logStream = new StreamWriter(logFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };
}
catch
{
    logFile = null;   // logging to a file is a convenience; the run continues without it
}

var writers = new List<IEventWriter> { human ? new HumanConsoleWriter(Console.Out) : new JsonlEventWriter(Console.Out) };
if (logStream is not null) writers.Add(new JsonlEventWriter(logStream));
IEventWriter log = new TeeEventWriter(writers.ToArray());

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// F12 emergency stop watcher.
var stopWatcher = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        if (NativeMethods.IsF12Down())
        {
            await log.WriteAsync(AgentEvent.Warn("agent.stop", ErrorCodes.UserCancelled, "Emergency stop (F12) pressed"), CancellationToken.None);
            cts.Cancel();
            return;
        }
        try { await Task.Delay(150, cts.Token); } catch (OperationCanceledException) { return; }
    }
});

var request = new AgentRunRequest
{
    StartMode = StartMode.CurrentVisibleExplorerFolder,
    MaxDepth = EnvInt("CLOUDSAVE_MAX_DEPTH", 10),
    EnableRecursion = EnvBool("CLOUDSAVE_RECURSION", true),
    MaxOfficeFiles = EnvInt("CLOUDSAVE_MAX_FILES", 0),
    EnableResume = EnvBool("CLOUDSAVE_RESUME", true),
    StartDelay = TimeSpan.FromSeconds(Math.Max(0, EnvInt("CLOUDSAVE_START_DELAY_SEC", 0))),
};

var stateFile = Environment.GetEnvironmentVariable("CLOUDSAVE_STATE_FILE");
if (string.IsNullOrWhiteSpace(stateFile))
    stateFile = Path.Combine(AppContext.BaseDirectory, "logs", "state.json");

var options = WindowsOptions.FromEnvironment();

await log.WriteAsync(AgentEvent.Info("agent.start",
    $"CloudSave Agent v3 (recursion={request.EnableRecursion}, maxFiles={request.MaxOfficeFiles}, resume={request.EnableResume}, log={logFile}). Emergency stop: F12.",
    new AgentStartInfo(request.EnableRecursion, request.MaxOfficeFiles, request.EnableResume, logFile)),
    cts.Token);

// With resume off (single-file tests) nothing is remembered, so a test can never make a
// later real run skip a file.
IStateStore state = request.EnableResume ? JsonStateStore.Load(stateFile) : new InMemoryStateStore();

var orchestrator = new AgentOrchestrator(
    log,
    new WindowsExplorerAdapter(log, options),
    new WindowsOfficeAdapter(log, options),
    new WindowsAnalyzerAdapter(log, options),
    state);

AgentRunResult result;
try
{
    result = await orchestrator.RunAsync(request, cts.Token);
}
finally
{
    cts.Cancel();      // stop the F12 watcher
}

var summary = new RunSummary(result.Root, result.Processed, result.Failed, result.Skipped, result.Unverified, result.Failures, logFile);
await log.WriteAsync(
    result.Success
        ? AgentEvent.Info("agent.complete",
            $"Processed={result.Processed}; Failed={result.Failed}; Skipped={result.Skipped}; Unverified={result.Unverified}", summary)
        : AgentEvent.Error("agent.failed", result.ErrorCode ?? "UNKNOWN", result.Message ?? "Agent failed", summary),
    CancellationToken.None);

logStream?.Dispose();
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
