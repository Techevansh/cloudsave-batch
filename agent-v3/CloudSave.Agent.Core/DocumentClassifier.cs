using System.Text.RegularExpressions;

namespace CloudSave.Agent.Core;

/// <summary>How one Explorer row was classified (see <see cref="DocumentClassifier.ClassifyRow"/>).</summary>
public sealed record RowClassification(bool IsFolder, DocumentKind Kind, bool LikelyHiddenOfficeFile);

/// <summary>
/// Pure, platform-agnostic classification of an Explorer row by its visible name.
/// Lives in Core so it is unit-testable without Windows. The Windows ExplorerAdapter
/// passes the row's visible cell texts (type / size / date columns) to
/// <see cref="ClassifyRow"/> so folders are told apart from files by evidence, not
/// only by the "no extension" heuristic.
/// </summary>
public static class DocumentClassifier
{
    private static readonly string[] PowerPointExt = { ".pptx" };
    private static readonly string[] ExcelExt = { ".xlsx", ".xls" };

    // Explorer "Type" column for a folder (ko / en).
    private static readonly string[] FolderTypeTexts = { "파일 폴더", "File folder" };

    // Explorer "Size" column: only files show a size ("12KB", "1,024 KB", "0바이트", "3 bytes").
    private static readonly Regex SizeText = new(
        @"^\s*\d[\d,\.]*\s*(KB|MB|GB|TB|B|바이트|bytes?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Office document kind for a file name, or Other for anything else.</summary>
    public static DocumentKind ClassifyFile(string name)
    {
        if (IsOfficeTempFile(name)) return DocumentKind.Other;
        var ext = GetExtension(name);
        if (Array.Exists(PowerPointExt, e => e == ext)) return DocumentKind.PowerPoint;
        if (Array.Exists(ExcelExt, e => e == ext)) return DocumentKind.Excel;
        return DocumentKind.Other;
    }

    /// <summary>Office owner/lock files ("~$name.pptx") appear while a document is open; never process them.</summary>
    public static bool IsOfficeTempFile(string name) =>
        !string.IsNullOrEmpty(name) && name.StartsWith("~$", StringComparison.Ordinal);

    /// <summary>True when the name has no file extension (the fallback folder heuristic).</summary>
    public static bool LooksLikeFolder(string name) => string.IsNullOrEmpty(GetExtension(name));

    /// <summary>True for the Office extensions the agent processes.</summary>
    public static bool IsSupportedOffice(string name) => ClassifyFile(name) != DocumentKind.Other;

    /// <summary>
    /// Classify an Explorer row from its name plus the texts Explorer shows for it.
    /// Order of evidence:
    ///   1. a supported Office extension            => that Office file
    ///   2. "file folder" type text                 => folder
    ///   3. a size value, or an Office type text    => a (non-processed) file
    ///   4. no evidence at all (e.g. icon view)     => fallback: no extension means folder
    /// Rule 3 is what keeps a file whose extension Explorer hides from being treated as a
    /// folder (which would open it instead of navigating). Such Office-looking files are
    /// flagged so the run can tell the user to turn on "File name extensions".
    /// </summary>
    public static RowClassification ClassifyRow(string name, IReadOnlyCollection<string>? cellTexts)
    {
        var kind = ClassifyFile(name);
        if (kind != DocumentKind.Other) return new RowClassification(false, kind, false);

        // The name cell repeats the row's own name; it is not evidence (a folder called
        // "Excel 자료" must not look like an Excel file).
        var texts = (cellTexts ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t.Trim(), name.Trim(), StringComparison.Ordinal))
            .ToList();
        if (texts.Any(t => FolderTypeTexts.Any(f => t.Contains(f, StringComparison.OrdinalIgnoreCase))))
            return new RowClassification(true, DocumentKind.Other, false);

        var officeTypeText = texts.Any(t =>
            t.Contains("PowerPoint", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Excel", StringComparison.OrdinalIgnoreCase));
        var hasSize = texts.Any(t => SizeText.IsMatch(t));

        if (hasSize || officeTypeText)
        {
            var hiddenOffice = officeTypeText && LooksLikeFolder(name) && !IsOfficeTempFile(name);
            return new RowClassification(false, DocumentKind.Other, hiddenOffice);
        }

        return new RowClassification(LooksLikeFolder(name), DocumentKind.Other, false);
    }

    private static string GetExtension(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return string.Empty;
        return name[dot..].ToLowerInvariant();
    }
}
