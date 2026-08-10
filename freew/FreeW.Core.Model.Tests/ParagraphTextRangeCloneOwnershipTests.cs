using System.IO;

namespace FreeW.Core.Model.Tests;

public sealed class ParagraphTextRangeCloneOwnershipTests
{
    [Fact]
    public void ShapeParagraphRangeCloning_IsOwnedByDocumentModelCloner()
    {
        var cloner = ReadSource("freew", "FreeW.Core.Model", "DocumentModelCloner.cs");
        var commands = ReadSource("freew", "FreeW.Core.Model", "EditCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        cloner.Should().Contain("public static Paragraph CloneParagraphTextRange(");
        commands.Should().Contain("DocumentModelCloner.CloneParagraphTextRange(");
        commands.Should().NotContain("private static Paragraph CloneParagraphWithTextRange(");
        avalonia.Should().Contain("DocumentModelCloner.CloneParagraphTextRange(");
        avalonia.Should().Contain("preserveUnselectedText: true");
        avalonia.Should().NotContain("private static Paragraph CloneShapeParagraphWithRange(");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }
}
