using CloudSave.Agent.Core;
using CloudSave.Agent.Core.Infrastructure;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class HumanConsoleWriterTests
{
    [Fact]
    public void Start_banner_explains_the_run_and_the_emergency_stop()
    {
        var text = HumanConsoleWriter.Render(AgentEvent.Info("agent.start", "x", new AgentStartInfo(true, 0, true, "C:\\l.jsonl")))!;
        Assert.Contains("하위 폴더 전체", text);
        Assert.Contains("F12", text);
        Assert.Contains("저장하지 않고 닫습니다", text);
        Assert.DoesNotContain("테스트 모드", text);

        var test = HumanConsoleWriter.Render(AgentEvent.Info("agent.start", "x", new AgentStartInfo(false, 1, false, null)))!;
        Assert.Contains("테스트 모드", test);
        Assert.Contains("하위 폴더 제외", test);
    }

    [Fact]
    public void Target_line_names_the_folder_and_counts()
    {
        var text = HumanConsoleWriter.Render(AgentEvent.Info("run.target", "x", new RunTarget("U:\\work", 5, 3, true, 10)));
        Assert.NotNull(text);
        Assert.Contains("U:\\work", text);
        Assert.Contains("Office 파일 5개", text);
        Assert.Contains("하위 폴더 3개", text);
    }

    [Fact]
    public void Countdown_tells_the_user_how_to_abort()
    {
        var text = HumanConsoleWriter.Render(AgentEvent.Info("run.countdown", "x", new RunTarget("U:\\work", 1, 0, true, 10)));
        Assert.Contains("10초", text);
        Assert.Contains("F12", text);
    }

    [Fact]
    public void File_progress_lines()
    {
        Assert.Equal("[3] deck.pptx", HumanConsoleWriter.Render(AgentEvent.Info("file.open", "p", new FileProgress(3, "deck.pptx"))));
        Assert.Contains("42초", HumanConsoleWriter.Render(AgentEvent.Info("file.complete", "p", new FileProgress(3, "deck.pptx", 41.8))));
        var failed = HumanConsoleWriter.Render(AgentEvent.Error("file.failed", ErrorCodes.UploadFailed, "m",
            new FileFailure("U:\\work\\deck.pptx", ErrorCodes.UploadFailed, "❌ 코드 500")));
        Assert.Contains(ErrorCodes.UploadFailed, failed);
        Assert.Contains("코드 500", failed);
    }

    [Fact]
    public void Diagnostics_and_routine_info_stay_out_of_the_console()
    {
        Assert.Null(HumanConsoleWriter.Render(AgentEvent.Info("analyzer.candidates", "PPTX analyzer count=1")));
        Assert.Null(HumanConsoleWriter.Render(AgentEvent.Warn("analyzer.diag", ErrorCodes.AnalyzerTaskpaneTimeout, "named elements")));
        Assert.Null(HumanConsoleWriter.Render(AgentEvent.Warn("retry.transient", ErrorCodes.UiaTransient, "retrying")));
    }

    [Fact]
    public void Explorer_warnings_and_unknown_errors_are_shown()
    {
        Assert.NotNull(HumanConsoleWriter.Render(AgentEvent.Warn("explorer.extensions", ErrorCodes.ExplorerExtensionHidden, "turn on extensions")));
        Assert.NotNull(HumanConsoleWriter.Render(AgentEvent.Error("something.new", ErrorCodes.Unexpected, "boom")));
    }

    [Fact]
    public void Summary_lists_counts_failures_and_the_log_file()
    {
        var summary = new RunSummary("U:\\work", 12, 1, 3, 1,
            new[] { new FileFailure("U:\\work\\a.pptx", ErrorCodes.UploadFailed, "❌ 코드 500") },
            "C:\\agent\\logs\\run.jsonl");
        var text = HumanConsoleWriter.Render(AgentEvent.Info("agent.complete", "done", summary))!;

        Assert.Contains("성공 12", text);
        Assert.Contains("실패 1", text);
        Assert.Contains("건너뜀 3", text);
        Assert.Contains("미확인 1", text);
        Assert.Contains("U:\\work\\a.pptx", text);
        Assert.Contains("C:\\agent\\logs\\run.jsonl", text);
    }

    [Fact]
    public void Aborted_run_summary_explains_the_reason()
    {
        var summary = new RunSummary("U:\\work", 2, 0, 0, 0, Array.Empty<FileFailure>(), null);
        var text = HumanConsoleWriter.Render(AgentEvent.Error("agent.failed", ErrorCodes.ExplorerLocationLost, "lost", summary))!;
        Assert.Contains(ErrorCodes.ExplorerLocationLost, text);
        Assert.Contains("이어서", text);
    }

    [Fact]
    public async Task Tee_keeps_writing_when_one_writer_fails()
    {
        var good = new FakeEvents();
        var tee = new TeeEventWriter(new ThrowingWriter(), good);
        await tee.WriteAsync(AgentEvent.Info("x", "y"), default);
        Assert.Single(good.Events);
    }

    private sealed class ThrowingWriter : IEventWriter
    {
        public Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken) => throw new IOException("disk full");
    }
}

public sealed class ResumeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cloudsave-resume-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("U:\\a\\b.pptx", "U:\\a", true)]
    [InlineData("U:\\a", "U:\\a", true)]
    [InlineData("U:\\ab\\c.pptx", "U:\\a", false)]   // sibling with a common prefix is NOT under
    [InlineData("anything", "", true)]
    public void Is_at_or_under(string path, string root, bool expected)
    {
        Assert.Equal(expected, LogicalPaths.IsAtOrUnder(path, root));
    }

    [Fact]
    public async Task Json_store_removes_only_records_under_the_root_and_persists_it()
    {
        var file = Path.Combine(_dir, "state.json");
        var store = JsonStateStore.Load(file);
        await store.UpsertAsync(new ProcessingRecord("U:\\a\\x.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);
        await store.UpsertAsync(new ProcessingRecord("U:\\a\\sub\\y.xlsx", ProcessingStatus.Failed, 2, DateTimeOffset.UtcNow, "e"), default);
        await store.UpsertAsync(new ProcessingRecord("U:\\ab\\z.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);

        Assert.Equal(2, await store.RemoveUnderAsync("U:\\a", default));

        var reloaded = JsonStateStore.Load(file);
        Assert.Null(await reloaded.GetAsync("U:\\a\\x.pptx", default));
        Assert.Null(await reloaded.GetAsync("U:\\a\\sub\\y.xlsx", default));
        Assert.NotNull(await reloaded.GetAsync("U:\\ab\\z.pptx", default));
    }

    [Fact]
    public async Task In_memory_store_supports_remove_under()
    {
        var store = new InMemoryStateStore();
        await store.UpsertAsync(new ProcessingRecord("R\\a.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);
        Assert.Equal(1, await store.RemoveUnderAsync("R", default));
        Assert.Null(await store.GetAsync("R\\a.pptx", default));
    }
}
