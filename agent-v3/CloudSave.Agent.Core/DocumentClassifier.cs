namespace CloudSave.Agent.Core;

/// <summary>
/// Pure, platform-agnostic classification of an Explorer row by its visible name.
/// Lives in Core so it is unit-testable without Windows. The Windows ExplorerAdapter
/// combines this with UIA metadata (e.g. the "file folder" type column) for folders
/// whose names contain a dot.
/// </summary>
public static class DocumentClassifier
{
    private static readonly string[] PowerPointExt = { ".pptx" };
    private static readonly string[] ExcelExt = { ".xlsx", ".xls" };

    /// <summary>Office document kind for a file name, or Other for anything else.</summary>
    public static DocumentKind ClassifyFile(string name)
    {
        var ext = GetExtension(name);
        if (Array.Exists(PowerPointExt, e => e == ext)) return DocumentKind.PowerPoint;
        if (Array.Exists(ExcelExt, e => e == ext)) return DocumentKind.Excel;
        return DocumentKind.Other;
    }

    /// <summary>True when the name has no file extension (the primary folder heuristic).</summary>
    public static bool LooksLikeFolder(string name) => string.IsNullOrEmpty(GetExtension(name));

    /// <summary>True for the Office extensions the agent processes.</summary>
    public static bool IsSupportedOffice(string name) => ClassifyFile(name) != DocumentKind.Other;

    private static string GetExtension(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return string.Empty;
        return name[dot..].ToLowerInvariant();
    }
}
