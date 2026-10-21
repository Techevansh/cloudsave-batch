using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

/// <summary>
/// Completion rules for one click of "구조 분석 시작", using the exact texts the add-ins
/// (taskpane.js / excel.js) put in the pane.
/// </summary>
public sealed class AnalyzerPaneTests
{
    private static readonly string[] Idle = { "🔍 구조 분석 시작", "📊 분석 준비됨. 검사를 시작하세요." };
    private static readonly string[] Busy = { "분석 중...", "📦 슬라이드 파싱 중...", "🧮 구조 무결성 검증 중..." };

    private static readonly string[] DoneBothOk =
    {
        "🔍 구조 분석 시작", "　✅ 레이아웃 구조 정합성 확인", "　✅ 미디어 요소 검증 완료", "─── 분석 리포트 완성 ───",
    };

    private static readonly string[] DoneOneFailed =
    {
        "🔍 구조 분석 시작", "　✅ 레이아웃 구조 정합성 확인", "　❌ 미디어 요소 검사 실패 (코드 500)", "─── 분석 리포트 완성 ───",
    };

    private static PaneSnapshot S(params string[] texts) => PaneSnapshot.FromTexts(texts);

    [Fact]
    public void Snapshot_reads_the_add_in_texts()
    {
        var s = S(DoneOneFailed);
        Assert.True(s.StartVisible);
        Assert.True(s.Done);
        Assert.False(s.Working);
        Assert.Equal(1, s.SuccessCount);
        Assert.Single(s.FailureLines);
        Assert.Null(s.StopText);
    }

    [Fact]
    public void Stop_line_is_not_counted_as_an_upload_failure()
    {
        var s = S("🔍 구조 분석 시작", "❌ 검사 중단: 인증이 취소되었습니다.");
        Assert.NotNull(s.StopText);
        Assert.Empty(s.FailureLines);
    }

    [Fact]
    public void Normal_run_succeeds_only_with_destination_lines()
    {
        var t = new AnalysisTracker();
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(Idle)));
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(Busy)));
        Assert.Equal(AnalysisVerdict.Succeeded, t.Observe(S(DoneBothOk)));
    }

    [Fact]
    public void Report_done_with_a_failed_destination_is_an_upload_failure()
    {
        var t = new AnalysisTracker();
        t.Observe(S(Busy));
        Assert.Equal(AnalysisVerdict.UploadFailed, t.Observe(S(DoneOneFailed)));
        Assert.Contains("코드 500", t.Detail);
        Assert.Single(t.Failures);
    }

    [Fact]
    public void Add_in_exception_is_stopped()
    {
        var t = new AnalysisTracker();
        t.Observe(S(Busy));
        Assert.Equal(AnalysisVerdict.Stopped, t.Observe(S("🔍 구조 분석 시작", "❌ 검사 중단: 파일 읽기 실패")));
        Assert.Contains("파일 읽기 실패", t.Detail);
    }

    [Fact]
    public void Previous_attempt_results_are_ignored_until_the_retry_is_underway()
    {
        // Right after a retry click the old report (with its ❌) is still on screen.
        var t = new AnalysisTracker();
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(DoneOneFailed)));
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(DoneOneFailed)));
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(Busy)));
        Assert.Equal(AnalysisVerdict.Succeeded, t.Observe(S(DoneBothOk)));
    }

    [Fact]
    public void Add_in_refusing_to_start_is_stopped_after_a_few_polls()
    {
        var t = new AnalysisTracker();
        t.Observe(S(Idle));
        var refusal = S("🔍 구조 분석 시작", "❌ 검사 항목을 하나 이상 선택해주세요.");
        for (var i = 1; i < AnalysisTracker.RefusalPolls; i++)
            Assert.Equal(AnalysisVerdict.Pending, t.Observe(refusal));
        Assert.Equal(AnalysisVerdict.Stopped, t.Observe(refusal));
    }

    [Fact]
    public void Restored_button_without_readable_results_becomes_unverified_not_success()
    {
        var t = new AnalysisTracker();
        t.Observe(S(Busy));
        var restored = S("🔍 구조 분석 시작");
        for (var i = 1; i < AnalysisTracker.UnverifiedPolls; i++)
            Assert.Equal(AnalysisVerdict.Pending, t.Observe(restored));
        Assert.Equal(AnalysisVerdict.SucceededUnverified, t.Observe(restored));
    }

    [Fact]
    public void Restored_button_never_completes_if_the_run_was_never_seen()
    {
        var t = new AnalysisTracker();
        for (var i = 0; i < AnalysisTracker.UnverifiedPolls * 3; i++)
            Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(Idle)));
    }

    [Fact]
    public void Report_without_any_destination_line_is_unverified()
    {
        var t = new AnalysisTracker();
        t.Observe(S(Busy));
        Assert.Equal(AnalysisVerdict.SucceededUnverified, t.Observe(S("🔍 구조 분석 시작", "─── 분석 리포트 완성 ───")));
    }

    [Fact]
    public void Long_sign_in_wait_stays_pending()
    {
        var t = new AnalysisTracker();
        var signingIn = S("분석 중...", "🔍 레이아웃 구조 스캔 중...");
        for (var i = 0; i < 100; i++)
            Assert.Equal(AnalysisVerdict.Pending, t.Observe(signingIn));
    }

    [Fact]
    public void Run_that_finishes_between_two_polls_is_recognised_from_a_clean_baseline()
    {
        var t = new AnalysisTracker();
        t.Baseline(S(Idle));
        Assert.Equal(AnalysisVerdict.Succeeded, t.Observe(S(DoneBothOk)));
    }

    [Fact]
    public void Fast_retry_with_a_different_result_is_recognised()
    {
        var t = new AnalysisTracker();
        t.Baseline(S(DoneOneFailed));                      // previous attempt still on screen
        Assert.Equal(AnalysisVerdict.Succeeded, t.Observe(S(DoneBothOk)));
    }

    [Fact]
    public void Retry_baseline_keeps_identical_old_results_pending()
    {
        var t = new AnalysisTracker();
        t.Baseline(S(DoneOneFailed));
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(DoneOneFailed)));
        Assert.Equal(AnalysisVerdict.Pending, t.Observe(S(Busy)));
        Assert.Equal(AnalysisVerdict.UploadFailed, t.Observe(S(DoneOneFailed)));
    }
}
