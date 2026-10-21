using CloudSave.Agent.Core;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class DocumentClassifierTests
{
    [Theory]
    [InlineData("deck.pptx", DocumentKind.PowerPoint)]
    [InlineData("DECK.PPTX", DocumentKind.PowerPoint)]
    [InlineData("book.xlsx", DocumentKind.Excel)]
    [InlineData("legacy.xls", DocumentKind.Excel)]
    [InlineData("notes.hwp", DocumentKind.Other)]
    [InlineData("clip.mp4", DocumentKind.Other)]
    [InlineData("image.png", DocumentKind.Other)]
    [InlineData("folderlike", DocumentKind.Other)]
    public void Classifies_by_extension(string name, DocumentKind expected)
    {
        Assert.Equal(expected, DocumentClassifier.ClassifyFile(name));
    }

    [Theory]
    [InlineData("deck.pptx", true)]
    [InlineData("book.xlsx", true)]
    [InlineData("legacy.xls", true)]
    [InlineData("notes.hwp", false)]
    [InlineData("folder", false)]
    public void Detects_supported_office(string name, bool supported)
    {
        Assert.Equal(supported, DocumentClassifier.IsSupportedOffice(name));
    }

    [Theory]
    [InlineData("003 old", true)]
    [InlineData("report", true)]
    [InlineData("deck.pptx", false)]
    [InlineData("trailingdot.", true)]
    // A folder whose name contains a dot looks extension-bearing to the pure heuristic;
    // the Windows ExplorerAdapter must confirm such rows via the UIA "file folder" type.
    [InlineData("003. old", false)]
    public void Looks_like_folder_when_no_extension(string name, bool isFolder)
    {
        Assert.Equal(isFolder, DocumentClassifier.LooksLikeFolder(name));
    }

    [Theory]
    [InlineData("~$deck.pptx")]
    [InlineData("~$book.xlsx")]
    public void Office_lock_files_are_never_processed(string name)
    {
        Assert.Equal(DocumentKind.Other, DocumentClassifier.ClassifyFile(name));
        Assert.False(DocumentClassifier.IsSupportedOffice(name));
    }

    public static IEnumerable<object[]> RowCases() => new[]
    {
        // name, cell texts, expected folder, expected kind, expected hidden-office flag
        new object[] { "report", new[] { "2026-10-01 오후 3:00", "파일 폴더" }, true, DocumentKind.Other, false },
        new object[] { "003. old", new[] { "파일 폴더" }, true, DocumentKind.Other, false },
        new object[] { "Archive", new[] { "File folder" }, true, DocumentKind.Other, false },
        // Hidden extension: an Office file must NOT be mistaken for a folder.
        new object[] { "deck", new[] { "Microsoft PowerPoint 프레젠테이션", "1,234KB" }, false, DocumentKind.Other, true },
        new object[] { "book", new[] { "Microsoft Excel 워크시트" }, false, DocumentKind.Other, true },
        new object[] { "readme", new[] { "텍스트 문서", "1KB" }, false, DocumentKind.Other, false },
        new object[] { "archive.zip", new[] { "압축(ZIP) 폴더", "12 KB" }, false, DocumentKind.Other, false },
        // No evidence (icon view): fall back to the extension heuristic.
        new object[] { "report", Array.Empty<string>(), true, DocumentKind.Other, false },
        // The row's own name cell is not evidence.
        new object[] { "Excel 자료", new[] { "Excel 자료" }, true, DocumentKind.Other, false },
        new object[] { "Excel 자료", new[] { "Excel 자료", "파일 폴더" }, true, DocumentKind.Other, false },
        // A supported extension wins without needing any cell text.
        new object[] { "deck.pptx", Array.Empty<string>(), false, DocumentKind.PowerPoint, false },
        new object[] { "legacy.xls", new[] { "Microsoft Excel 97-2003 워크시트", "40KB" }, false, DocumentKind.Excel, false },
        new object[] { "~$deck.pptx", new[] { "1KB" }, false, DocumentKind.Other, false },
    };

    [Theory]
    [MemberData(nameof(RowCases))]
    public void Classifies_rows_by_evidence(string name, string[] cells, bool folder, DocumentKind kind, bool hiddenOffice)
    {
        var c = DocumentClassifier.ClassifyRow(name, cells);
        Assert.Equal(folder, c.IsFolder);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(hiddenOffice, c.LikelyHiddenOfficeFile);
    }
}
