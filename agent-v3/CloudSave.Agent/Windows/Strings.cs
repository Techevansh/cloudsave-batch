namespace CloudSave.Agent.Windows;

/// <summary>
/// Korean (and English fallback) Windows / Office UI strings the adapters look for. The
/// add-ins' own task-pane texts live in Core (<see cref="CloudSave.Agent.Core.AnalyzerPaneText"/>)
/// so their completion rules are unit-tested. Unlike Windows PowerShell 5.1, the C# compiler
/// reads UTF-8 source correctly, so these are plain literals.
/// </summary>
internal static class Strings
{
    // Explorer
    public const string Back = "뒤로";                       // Back button ("뒤로", "뒤로(Alt+왼쪽 화살표)" ...)
    public const string BackEn = "Back";
    public const string Up = "위로";                         // Up-one-level button
    public const string UpEn = "Up";
    public const string AddressPrefix = "주소:";             // breadcrumb toolbar name "주소: U:\..."
    public const string AddressPrefixEn = "Address:";

    // Explorer item rows / items view (DirectUI class names)
    public const string ExplorerRowClass = "UIItem";
    public const string ExplorerItemsViewClass = "UIItemsView";

    // Office
    public const string EnableEditing = "편집 사용";         // Protected View banner
    public const string EnableEditingEn = "Enable Editing";
    public const string HomeTab = "홈";                      // ribbon Home tab
    public const string HomeTabEn = "Home";

    // Office prompts: only ever the non-modifying choice.
    public const string DontSave = "저장 안";                // "저장 안 함" / "저장 안함"
    public const string DontSaveEn = "Don't Save";
    public const string DontSaveEnTypographic = "Don’t Save";
    public const string DontUpdate = "업데이트 안 함";       // Excel "update links?" prompt
    public const string DontUpdateEn = "Don't Update";
    public const string ReadOnly = "읽기 전용";               // "file in use" prompt: open read-only
    public const string ReadOnlyEn = "Read Only";

    // Ribbon add-in buttons (ASCII in the manifests, so no localization needed).
    public const string PptxAnalyzer = "PPTX analyzer";
    public const string ExcelAnalyzer = "Excel analyzer";
}
