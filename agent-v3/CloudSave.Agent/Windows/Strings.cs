namespace CloudSave.Agent.Windows;

/// <summary>
/// Korean UI strings the adapters look for. Unlike Windows PowerShell 5.1, the C#
/// compiler reads UTF-8 source correctly, so these are plain literals (no code-point
/// assembly needed). This whole class of encoding/parser bugs is gone in v3.
/// </summary>
internal static class Strings
{
    public const string AnalysisStart = "구조 분석 시작";   // blue task-pane button
    public const string Analyzing = "분석 중...";            // button text while running
    public const string ReportDone = "분석 리포트 완성";     // completion text
    public const string InspectStopped = "검사 중단";        // failure/stop text
    public const string FileFolderType = "파일 폴더";        // Explorer "type" column for a folder
    public const string Back = "뒤로";                       // Explorer Back button
    public const string EnableEditing = "편집 사용";         // Protected View banner
    public const string HomeTab = "홈";                      // ribbon Home tab
    public const string DontSave = "저장 안 함";             // save prompt: discard

    // Ribbon add-in buttons (ASCII in the manifests, so no localization needed).
    public const string PptxAnalyzer = "PPTX analyzer";
    public const string ExcelAnalyzer = "Excel analyzer";

    // English fallbacks for non-localized Office/Explorer surfaces.
    public const string BackEn = "Back";
    public const string EnableEditingEn = "Enable Editing";
    public const string HomeTabEn = "Home";
    public const string DontSaveEn = "Don't Save";
}
