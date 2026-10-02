using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class AgentOrchestratorTests
{
    private static AgentOrchestrator Build(
        FakeEvents events, FakeExplorer explorer, FakeOffice office, FakeAnalyzer analyzer, FakeStateStore state) =>
        new(events, explorer, office, analyzer, state, RetryPolicy.Immediate());

    private static AgentRunRequest Req(bool recursion = true, int maxFiles = 0, bool resume = true, int maxDepth = 10) =>
        new() { EnableRecursion = recursion, MaxOfficeFiles = maxFiles, EnableResume = resume, MaxDepth = maxDepth };

    // ---- Resume -------------------------------------------------------------

    [Fact]
    public async Task Skips_previously_completed_file()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("done.pptx") };

        var state = new FakeStateStore();
        await state.UpsertAsync(new ProcessingRecord("done.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);

        var events = new FakeEvents();
        var office = new FakeOffice();
        var result = await Build(events, explorer, office, new FakeAnalyzer(), state).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(0, result.Processed);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(explorer.Activated);
        Assert.Equal(0, office.CloseCount);
    }

    [Fact]
    public async Task Resume_disabled_reprocesses_completed_file()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("done.pptx") };
        var state = new FakeStateStore();
        await state.UpsertAsync(new ProcessingRecord("done.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), state)
            .RunAsync(Req(resume: false), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.Single(explorer.Activated);
    }

    // ---- Classification / selection ----------------------------------------

    [Fact]
    public async Task Processes_only_office_files()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new()
        {
            FakeExplorer.File("a.pptx"),
            FakeExplorer.File("b.xlsx"),
            FakeExplorer.File("c.hwp"),
            FakeExplorer.File("d.mp4"),
        };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(2, result.Processed);
        Assert.Equal(new[] { "a.pptx", "b.xlsx" }, explorer.Activated);
    }

    // ---- Failure isolation --------------------------------------------------

    [Fact]
    public async Task One_file_failure_does_not_abort_run()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("bad.pptx"), FakeExplorer.File("good.pptx") };

        var analyzer = new FakeAnalyzer
        {
            RunOverride = s => s.Title == "bad.pptx"
                ? AnalysisResult.Failure(ErrorCodes.AnalysisTimeout, "boom")
                : AnalysisResult.Completed(false)
        };

        var office = new FakeOffice();
        var events = new FakeEvents();
        var result = await Build(events, explorer, office, analyzer, new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(2, office.CloseCount);           // both closed
        Assert.True(events.HasCode(ErrorCodes.AnalysisTimeout));
    }

    [Fact]
    public async Task Office_closed_even_when_analysis_throws()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("x.pptx") };
        var analyzer = new FakeAnalyzer { EnsureFailure = _ => new AgentOperationException(ErrorCodes.AnalyzerTaskpaneTimeout, "no pane") };
        var office = new FakeOffice();

        var result = await Build(new FakeEvents(), explorer, office, analyzer, new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(result.Success);   // run completes; the single file failed
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, office.CloseCount);
    }

    // ---- Recursion ----------------------------------------------------------

    [Fact]
    public async Task Recursion_disabled_skips_subfolders()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("sub"), FakeExplorer.File("top.pptx") };
        explorer.Rows["sub"] = new() { FakeExplorer.File("inner.pptx") };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(recursion: false), default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(new[] { "top.pptx" }, explorer.Activated);
        Assert.Empty(explorer.Entered);
    }

    [Fact]
    public async Task Recursion_enabled_processes_nested_and_navigates()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("sub"), FakeExplorer.File("top.pptx") };
        explorer.Rows["sub"] = new() { FakeExplorer.File("inner.pptx") };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.Equal(2, result.Processed);
        Assert.Contains("inner.pptx", explorer.Activated);
        Assert.Equal(new[] { "sub" }, explorer.Entered);
        Assert.Equal(1, explorer.BackCount);
    }

    [Fact]
    public async Task GoBack_called_even_when_child_read_throws()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("sub") };
        // First read of "sub" (the traversal snapshot) throws a non-transient error.
        explorer.ReadFailure = (folder, _) => folder == "sub"
            ? new AgentOperationException(ErrorCodes.Unexpected, "sub read failed")
            : null;

        var explorerRowsSub = new List<ExplorerItem>();
        explorer.Rows["sub"] = explorerRowsSub;

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Single(explorer.Entered);
        Assert.Equal(1, explorer.BackCount);   // climbed back out despite child failure
    }

    [Fact]
    public async Task Office_open_failure_is_isolated_and_nothing_to_close()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("x.pptx"), FakeExplorer.File("y.pptx") };
        var office = new FakeOffice { WaitFailure = f => f.Name == "x.pptx"
            ? new AgentOperationException(ErrorCodes.OfficeOpenTimeout, "never appeared")
            : null };

        var events = new FakeEvents();
        var result = await Build(events, explorer, office, new FakeAnalyzer(), new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);                 // y.pptx still processed
        Assert.Equal(1, result.Failed);                    // x.pptx failed to open
        Assert.Equal(1, office.CloseCount);                // only y.pptx had a window to close
        Assert.True(events.HasCode(ErrorCodes.OfficeOpenTimeout));
    }

    [Fact]
    public async Task Folder_enter_failure_is_isolated_without_goback()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("locked") };
        explorer.Rows["locked"] = new() { FakeExplorer.File("inner.pptx") };
        explorer.EnterFailure = f => f.LogicalPath == "locked"
            ? new AgentOperationException(ErrorCodes.ExplorerNavigationTimeout, "cannot enter")
            : null;

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, explorer.BackCount);   // never entered => never go back
        Assert.Empty(explorer.Activated);
    }

    [Fact]
    public async Task Folder_that_disappears_on_reread_is_skipped_not_fatal()
    {
        var explorer = new FakeExplorer();
        // First read lists "ghost"; the re-read before entering no longer lists it.
        var first = new List<ExplorerItem> { FakeExplorer.Folder("ghost") };
        explorer.Rows[""] = first;
        explorer.ReadFailure = (folder, call) =>
        {
            if (folder == "" && call >= 1) explorer.Rows[""] = new List<ExplorerItem>(); // disappear on re-read
            return null;
        };

        var events = new FakeEvents();
        var result = await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Failed);
        Assert.Empty(explorer.Entered);
        Assert.True(events.HasCode(ErrorCodes.ExplorerRowNotFound));
    }

    [Fact]
    public async Task Max_depth_is_respected()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("a") };
        explorer.Rows["a"] = new() { FakeExplorer.File("deep.pptx") };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(maxDepth: 0), default);

        // depth 0 reads root; entering "a" makes depth 1 > 0 => skipped.
        Assert.Equal(0, result.Processed);
        Assert.True(result.Skipped >= 1);
        Assert.Empty(explorer.Activated);
    }

    // ---- File cap (phased testing) -----------------------------------------

    [Fact]
    public async Task Max_office_files_caps_attempts()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("one.pptx"), FakeExplorer.File("two.pptx") };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(maxFiles: 1), default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(new[] { "one.pptx" }, explorer.Activated);
    }

    // ---- Auth ---------------------------------------------------------------

    [Fact]
    public async Task Interactive_auth_is_surfaced()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("login.pptx") };
        var analyzer = new FakeAnalyzer { InteractiveAuth = true };

        var events = new FakeEvents();
        var result = await Build(events, explorer, new FakeOffice(), analyzer, new FakeStateStore()).RunAsync(Req(), default);

        Assert.Equal(1, result.Processed);
        Assert.True(events.Has("auth.interactive"));
    }

    // ---- Transient retry ----------------------------------------------------

    [Fact]
    public async Task Transient_read_is_retried_then_succeeds()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("r.pptx") };
        var calls = 0;
        explorer.ReadFailure = (folder, _) =>
        {
            if (folder == "" && calls++ < 2)
                return new AgentOperationException(ErrorCodes.UiaTransient, "flaky");
            return null;
        };

        var events = new FakeEvents();
        var result = await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.True(events.HasCode(ErrorCodes.UiaTransient));   // retry warnings emitted
    }

    [Fact]
    public async Task Transient_read_exhausted_fails_run_with_code()
    {
        var explorer = new FakeExplorer();
        explorer.ReadFailure = (_, _) => new AgentOperationException(ErrorCodes.TaskpaneUiaTransient, "always flaky");

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.TaskpaneUiaTransient, result.ErrorCode);
    }

    // ---- Cancellation -------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_returns_user_cancelled()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("x.pptx") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UserCancelled, result.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_during_analysis_still_closes_office()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("x.pptx") };
        using var cts = new CancellationTokenSource();
        var office = new FakeOffice();
        var analyzer = new FakeAnalyzer { OnRun = _ => cts.Cancel() };

        var result = await Build(new FakeEvents(), explorer, office, analyzer, new FakeStateStore())
            .RunAsync(Req(), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UserCancelled, result.ErrorCode);
        Assert.Equal(1, office.CloseCount);   // closed in finally despite cancellation
    }

    // ---- State store --------------------------------------------------------

    [Fact]
    public async Task Failure_is_recorded_with_status_and_error()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("x.pptx") };
        var analyzer = new FakeAnalyzer { RunOverride = _ => AnalysisResult.Failure(ErrorCodes.AnalysisTimeout, "slow") };
        var state = new FakeStateStore();

        await Build(new FakeEvents(), explorer, new FakeOffice(), analyzer, state).RunAsync(Req(), default);

        var rec = state.Current("x.pptx");
        Assert.NotNull(rec);
        Assert.Equal(ProcessingStatus.Failed, rec!.Status);
        Assert.Equal(1, rec.Attempts);
        Assert.False(string.IsNullOrEmpty(rec.LastError));
    }
}
