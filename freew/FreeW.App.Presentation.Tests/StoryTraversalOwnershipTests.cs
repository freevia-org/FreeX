using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class StoryTraversalOwnershipTests
{
    [Fact]
    public void CommentListPlanner_PreservesDirectOutOfBodyReachWithoutTextBoxExpansion()
    {
        var direct = CommentParagraph(1);
        var nested = CommentParagraph(2);
        direct.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { nested } },
        });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body"));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(direct);
        document.Comments[1] = new Comment(1, "direct", "A", "A");
        document.Comments[2] = new Comment(2, "nested", "A", "A");

        CommentListPlanner.Build(document).Select(item => item.Id).Should().Equal(1);
    }

    [Fact]
    public void StorySubsetOwnership_IsSharedByAllFourAdopters()
    {
        var planner = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "CommentListPlanner.cs");
        var inspector = ReadSource("freew", "FreeW.Core.Model", "DocumentInspector.cs");
        var comments = ReadSource("freew", "FreeW.Core.Model", "CommentCommands.cs");
        var notes = ReadSource("freew", "FreeW.Core.Model", "NoteCommands.cs");

        foreach (var source in new[] { planner, inspector, comments, notes })
            source.Should().Contain("TextDocumentStoryTraversal.EnumerateParagraphs(");

        planner.Should().NotContain("foreach (var section in document.Sections)");
        comments.Should().NotContain("public static IEnumerable<Paragraph> ParagraphsInBlock");
        notes.Should().NotContain("private static IEnumerable<Paragraph> EnumerateHeaderFooterParagraphs");
    }

    private static Paragraph CommentParagraph(int id)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text") { CommentId = id });
        paragraph.Runs.Add(Run.CommentReference(id));
        return paragraph;
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
