namespace CloudSave.Agent.Core.Infrastructure;

/// <summary>
/// Human-readable (Korean) progress for the console launcher. The JSONL protocol is unchanged
/// and still goes to the log file (and to stdout in GUI mode); this writer only renders the
/// events a person watching the run needs, and skips diagnostics noise.
/// Plain-text tags (no emoji) so the classic Windows console renders every line.
/// </summary>
public sealed class HumanConsoleWriter : IEventWriter
{
    private readonly TextWriter _out;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HumanConsoleWriter(TextWriter output)
    {
        _out = output;
    }

    public async Task WriteAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        var text = Render(evt);
        if (text is null) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _out.WriteLineAsync(text).ConfigureAwait(false);
            await _out.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The console text for an event, or null when the event is not shown to people.</summary>
    public static string? Render(AgentEvent evt)
    {
        switch (evt.Type)
        {
            case "agent.start":
                return evt.Data is AgentStartInfo info ? RenderBanner(info) : "CloudSave 에이전트 시작 (비상 정지: F12)";

            case "run.target" when evt.Data is RunTarget t:
                return $"대상 폴더: {t.Root}{Environment.NewLine}" +
                       $"  이 폴더의 Office 파일 {t.OfficeFiles}개 / 하위 폴더 {t.Folders}개 " +
                       (t.Recursion ? "(하위 폴더 안의 파일도 모두 처리)" : "(하위 폴더는 처리하지 않음)");

            case "run.countdown" when evt.Data is RunTarget t:
                return $"{t.StartDelaySeconds}초 후 시작합니다. 대상 폴더가 다르면 지금 F12를 누르세요.";

            case "folder.enter":
                return $"[폴더] {evt.Message}";

            case "file.open" when evt.Data is FileProgress p:
                return $"[{p.Index}] {p.Name}";

            case "auth.window":
                return "    로그인 창이 열렸습니다 - 직접 로그인해 주세요 (에이전트가 기다립니다)";

            case "analysis.retry":
                return "    업로드 실패가 있어 한 번 더 시도합니다";

            case "file.complete" when evt.Data is FileProgress p:
                return p.Seconds is double s ? $"    -> 완료 ({s:0}초)" : "    -> 완료";

            case "file.unverified":
                return "    -> 주의: 끝났지만 업로드 결과 문구를 확인하지 못했습니다 (클라우드에서 확인 필요)";

            case "file.failed":
                return $"    -> 실패 [{evt.Code}] {evt.Data switch { FileFailure f => f.Message, _ => evt.Message }}";

            case "file.skip":
                return $"[건너뜀] 이미 완료: {evt.Message.Replace("Already completed: ", "")}";

            case "folder.failed":
            case "folder.missing":
            case "folder.read":
                return $"[폴더 실패] [{evt.Code}] {evt.Message}";

            case "folder.skip":
                return $"[건너뜀] {evt.Message}";

            case "agent.stop":
                return "[중지] F12 - 멈춥니다 (열려 있는 파일은 저장하지 않고 닫습니다)";

            case "state.cleared":
                return "모든 파일 완료 -> 이어하기 기록을 비웠습니다 (다음 실행은 처음부터 합니다)";

            case "state.kept":
                return "실패한 파일이 있습니다 -> 같은 폴더에서 다시 실행하면 실패한 파일만 다시 처리합니다";

            case "agent.complete":
            case "agent.failed":
                return evt.Data is RunSummary sum ? RenderSummary(sum, evt) : evt.Message;

            case "agent.exception":
                return $"[중단] [{evt.Code}] {evt.Message}";

            case "office.prompt":
                return $"    Office 확인 창에 '변경 없음' 쪽으로 자동 응답: {evt.Message.Replace("Answered open prompt ", "")}";
        }

        // Adapter warnings the user can act on (hidden extensions, incomplete listing, a window left open, ...).
        if (evt.Level == "warn"
            && (evt.Type.StartsWith("explorer.", StringComparison.Ordinal) || evt.Type.StartsWith("office.", StringComparison.Ordinal)))
            return $"[주의] {evt.Message}";

        // Anything else at error level is worth a line; info/warn diagnostics stay in the log file.
        if (evt.Level == "error" && evt.Type != "analyzer.diag")
            return $"[오류] [{evt.Code}] {evt.Message}";

        return null;
    }

    private static string RenderBanner(AgentStartInfo info)
    {
        var lines = new List<string>
        {
            "============================================================",
            " CloudSave 에이전트 - 폴더 전체 클라우드 저장",
            "============================================================",
            info.Recursion
                ? " - 탐색기에 열어 둔 폴더와 그 하위 폴더 전체의 .pptx / .xlsx / .xls 를"
                : " - 탐색기에 열어 둔 폴더(하위 폴더 제외)의 .pptx / .xlsx / .xls 를",
            "   하나씩 열어 애드인으로 클라우드에 저장하고, 저장하지 않고 닫습니다.",
            " - 실행 중에는 마우스와 키보드를 건드리지 마세요.   비상 정지: F12",
            " - 로그인 창이 뜨면 직접 로그인하시면 됩니다 (에이전트가 기다립니다).",
        };
        if (info.MaxOfficeFiles > 0)
            lines.Add($" - 테스트 모드: Office 파일 {info.MaxOfficeFiles}개만 처리합니다.");
        if (info.Resume)
            lines.Add(" - 중간에 멈춰도 같은 폴더에서 다시 실행하면 끝난 파일은 건너뛰고 이어서 합니다.");
        lines.Add("------------------------------------------------------------");
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderSummary(RunSummary s, AgentEvent evt)
    {
        var lines = new List<string>
        {
            "",
            "======================== 결과 ========================",
            $" 대상: {s.Root}",
            $" 성공 {s.Processed} / 실패 {s.Failed} / 건너뜀 {s.Skipped}",
        };
        if (s.Unverified > 0)
            lines.Add($" 업로드 결과 미확인 {s.Unverified}개 (클라우드에서 직접 확인해 주세요)");

        if (evt.Type == "agent.failed")
            lines.Add($" 중단 사유: {Hint(evt.Code)} [{evt.Code}]");

        if (s.Failures.Count > 0)
        {
            lines.Add(" 실패 목록:");
            foreach (var f in s.Failures)
                lines.Add($"  - {f.LogicalPath}  [{f.Code}] {f.Message}");
        }

        if (!string.IsNullOrEmpty(s.LogFile))
            lines.Add($" 로그 파일: {s.LogFile}");
        lines.Add("======================================================");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Plain-language reason for the codes that can stop a whole run.</summary>
    public static string Hint(string? code) => code switch
    {
        ErrorCodes.ExplorerNotFound => "처리할 폴더를 연 탐색기 창을 찾지 못했습니다. 폴더를 탐색기로 열어 두고 다시 실행하세요.",
        ErrorCodes.ExplorerLocationLost => "탐색기가 예상한 폴더로 돌아오지 않아 안전하게 멈췄습니다. 다시 실행하면 이어서 합니다.",
        ErrorCodes.UserCancelled => "F12로 중지했습니다. 다시 실행하면 이어서 합니다.",
        _ => "예상하지 못한 오류로 멈췄습니다. 로그 파일을 보내 주세요.",
    };
}
