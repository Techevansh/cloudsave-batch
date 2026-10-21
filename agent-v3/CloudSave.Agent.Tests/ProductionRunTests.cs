using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

/// <summary>
/// The behaviours a real "open the top folder, run the launcher" pass depends on:
/// safe navigation, only driving windows the agent opened, honest upload accounting,
/// the resume rule, and the start announcement / countdown.
/// </summary>
public sealed class ProductionRunTests
{
    private static AgentOrchestrator Build(
        FakeEvents events, FakeExplorer explorer, FakeOffice office, FakeAnalyzer analyzer, FakeStateStore state,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(events, explorer, office, analyzer, state, RetryPolicy.Immediate(), delay ?? ((_, _) => Task.CompletedTask));

    private static AgentRunRequest Req(bool resume = true, int maxFiles = 0, TimeSpan? startDelay = null) =>
        new() { EnableRecursion = true, EnableResume = resume, MaxOfficeFiles = maxFiles, StartDelay = startDelay ?? TimeSpan.Zero };

    // ---- Only drive the window this open produced -------------------------------

    [Fact]
    public async Task Office_baseline_is_taken_before_the_file_is_activated()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx") };
        var office = new FakeOffice();
        var prepareSeenAtActivation = false;
        explorer.OnActivate = f => prepareSeenAtActivation = office.Calls.Contains("prepare:" + f.LogicalPath);

        await Build(new FakeEvents(), explorer, office, new FakeAnalyzer(), new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(prepareSeenAtActivation);
        Assert.Equal(new[] { "prepare:a.pptx", "wait:a.pptx" }, office.Calls);
    }

    // ---- Navigation safety ---------------------------------------------------------

    [Fact]
    public async Task GoBack_failure_stops_the_run_instead_of_continuing_in_the_wrong_folder()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("a"), FakeExplorer.Folder("b") };
        explorer.Rows["a"] = new() { FakeExplorer.File("a1.pptx", "a\\a1.pptx") };
        explorer.Rows["b"] = new() { FakeExplorer.File("b1.pptx", "b\\b1.pptx") };
        explorer.GoBackFailure = _ => new AgentOperationException(ErrorCodes.ExplorerNavigationTimeout, "Back button not found");

        var events = new FakeEvents();
        var result = await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ExplorerLocationLost, result.ErrorCode);
        Assert.Equal(new[] { "a" }, explorer.Entered);          // never wandered into "b"
        Assert.Equal(new[] { "a\\a1.pptx" }, explorer.Activated);
        Assert.Equal(1, result.Processed);                     // work done before the stop is reported
        Assert.Equal(1, explorer.BackCount);
    }

    [Fact]
    public async Task Location_lost_deep_in_the_tree_does_not_attempt_further_navigation()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("a") };
        explorer.Rows["a"] = new() { FakeExplorer.Folder("aa", "a\\aa") };
        explorer.Rows["a\\aa"] = new() { FakeExplorer.File("x.pptx", "a\\aa\\x.pptx") };
        explorer.GoBackFailure = n => n == 0
            ? new AgentOperationException(ErrorCodes.ExplorerLocationLost, "not back in a")
            : null;

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(Req(), default);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ExplorerLocationLost, result.ErrorCode);
        Assert.Equal(1, explorer.BackCount);                   // no second Back from "a" while lost
    }

    [Fact]
    public async Task Folders_beyond_max_depth_are_not_entered()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.Folder("a") };
        explorer.Rows["a"] = new() { FakeExplorer.File("deep.pptx", "a\\deep.pptx") };

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore())
            .RunAsync(new AgentRunRequest { MaxDepth = 0 }, default);

        Assert.True(result.Success);
        Assert.Empty(explorer.Entered);
        Assert.Equal(0, explorer.BackCount);
        Assert.Equal(1, result.Skipped);
    }

    // ---- Honest upload accounting --------------------------------------------------

    [Fact]
    public async Task Upload_failure_is_a_failed_file_and_listed_in_the_result()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx"), FakeExplorer.File("b.xlsx") };
        var analyzer = new FakeAnalyzer
        {
            RunOverride = s => s.Title == "a.pptx"
                ? AnalysisResult.Failure(ErrorCodes.UploadFailed, "❌ 레이아웃 구조 검사 실패 (코드 500)")
                : AnalysisResult.Completed(false)
        };
        var office = new FakeOffice();

        var result = await Build(new FakeEvents(), explorer, office, analyzer, new FakeStateStore()).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Failed);
        var f = Assert.Single(result.Failures);
        Assert.Equal("a.pptx", f.LogicalPath);
        Assert.Equal(ErrorCodes.UploadFailed, f.Code);
        Assert.Equal(2, office.CloseCount);                    // the failed file is still closed
    }

    [Fact]
    public async Task Unverified_completion_is_counted_and_warned()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx") };
        var analyzer = new FakeAnalyzer { RunOverride = _ => AnalysisResult.Completed(false, verified: false) };
        var events = new FakeEvents();

        var result = await Build(events, explorer, new FakeOffice(), analyzer, new FakeStateStore()).RunAsync(Req(), default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Unverified);
        Assert.True(events.HasCode(ErrorCodes.UploadUnverified));
    }

    // ---- Resume rule -----------------------------------------------------------------

    [Fact]
    public async Task Clean_full_pass_clears_this_folders_records_only()
    {
        var explorer = new FakeExplorer { StartContext = "R" };
        explorer.Rows["R"] = new() { FakeExplorer.File("a.pptx", "R\\a.pptx") };
        var state = new FakeStateStore();
        await state.UpsertAsync(new ProcessingRecord("R\\old.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);
        await state.UpsertAsync(new ProcessingRecord("Other\\x.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);
        var events = new FakeEvents();

        var result = await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), state).RunAsync(Req(), default);

        Assert.True(result.Success);
        Assert.Null(state.Current("R\\a.pptx"));
        Assert.Null(state.Current("R\\old.pptx"));
        Assert.NotNull(state.Current("Other\\x.pptx"));         // another folder's records untouched
        Assert.True(events.Has("state.cleared"));
    }

    [Fact]
    public async Task Failures_keep_records_and_the_next_run_retries_only_the_failed_file()
    {
        var explorer = new FakeExplorer { StartContext = "R" };
        explorer.Rows["R"] = new() { FakeExplorer.File("bad.pptx", "R\\bad.pptx"), FakeExplorer.File("good.pptx", "R\\good.pptx") };
        var state = new FakeStateStore();
        var failBad = true;
        var analyzer = new FakeAnalyzer
        {
            RunOverride = s => failBad && s.Title == "bad.pptx"
                ? AnalysisResult.Failure(ErrorCodes.UploadFailed, "net")
                : AnalysisResult.Completed(false)
        };

        var events1 = new FakeEvents();
        var first = await Build(events1, explorer, new FakeOffice(), analyzer, state).RunAsync(Req(), default);
        Assert.Equal(1, first.Failed);
        Assert.True(events1.Has("state.kept"));
        Assert.Equal(ProcessingStatus.Completed, state.Current("R\\good.pptx")!.Status);

        failBad = false;
        var explorer2 = new FakeExplorer { StartContext = "R" };
        explorer2.Rows["R"] = explorer.Rows["R"];
        var second = await Build(new FakeEvents(), explorer2, new FakeOffice(), analyzer, state).RunAsync(Req(), default);

        Assert.Equal(new[] { "R\\bad.pptx" }, explorer2.Activated);   // good.pptx skipped
        Assert.Equal(1, second.Skipped);
        Assert.Equal(0, second.Failed);
        Assert.Equal(0, state.Count);                                 // clean pass => cleared
    }

    [Fact]
    public async Task Capped_test_run_never_clears_records()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx"), FakeExplorer.File("b.pptx") };
        var state = new FakeStateStore();

        await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), state).RunAsync(Req(maxFiles: 1), default);

        Assert.Equal(ProcessingStatus.Completed, state.Current("a.pptx")!.Status);
    }

    // ---- Start announcement / countdown ------------------------------------------------

    [Fact]
    public async Task Target_is_announced_with_counts_and_the_countdown_is_honoured()
    {
        var explorer = new FakeExplorer { StartContext = "U:\\work" };
        explorer.Rows["U:\\work"] = new()
        {
            FakeExplorer.File("a.pptx", "U:\\work\\a.pptx"),
            FakeExplorer.File("b.xlsx", "U:\\work\\b.xlsx"),
            FakeExplorer.File("c.pdf", "U:\\work\\c.pdf"),
            FakeExplorer.Folder("sub", "U:\\work\\sub"),
        };
        var delays = new List<TimeSpan>();
        var events = new FakeEvents();

        await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore(),
                (t, _) => { delays.Add(t); return Task.CompletedTask; })
            .RunAsync(Req(startDelay: TimeSpan.FromSeconds(10)), default);

        var target = Assert.IsType<RunTarget>(events.Events.First(e => e.Type == "run.target").Data);
        Assert.Equal("U:\\work", target.Root);
        Assert.Equal(2, target.OfficeFiles);
        Assert.Equal(1, target.Folders);
        Assert.Equal(new[] { TimeSpan.FromSeconds(10) }, delays);
        var countdownIndex = events.Events.FindIndex(e => e.Type == "run.countdown");
        var firstOpenIndex = events.Events.FindIndex(e => e.Type == "file.open");
        Assert.True(countdownIndex >= 0 && countdownIndex < firstOpenIndex);
    }

    [Fact]
    public async Task F12_during_the_countdown_cancels_before_any_file_is_opened()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx") };
        using var cts = new CancellationTokenSource();

        var result = await Build(new FakeEvents(), explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore(),
                (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; })
            .RunAsync(Req(startDelay: TimeSpan.FromSeconds(10)), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UserCancelled, result.ErrorCode);
        Assert.Empty(explorer.Activated);
    }

    [Fact]
    public async Task No_countdown_when_start_delay_is_zero()
    {
        var explorer = new FakeExplorer();
        explorer.Rows[""] = new() { FakeExplorer.File("a.pptx") };
        var delays = 0;
        var events = new FakeEvents();

        await Build(events, explorer, new FakeOffice(), new FakeAnalyzer(), new FakeStateStore(),
                (_, _) => { delays++; return Task.CompletedTask; })
            .RunAsync(Req(), default);

        Assert.Equal(0, delays);
        Assert.False(events.Has("run.countdown"));
        Assert.True(events.Has("run.target"));
    }
}
