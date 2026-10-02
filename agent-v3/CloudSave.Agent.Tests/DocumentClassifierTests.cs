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
}
