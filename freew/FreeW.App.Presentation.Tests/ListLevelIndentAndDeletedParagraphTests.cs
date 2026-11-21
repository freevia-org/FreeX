using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ListLevelIndentAndDeletedParagraphTests
{
    private static (TextDocument Document, Paragraph Paragraph) ListItem(ParagraphFormatting formatting)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph { Formatting = formatting };
        document.Blocks.Add(paragraph);
        return (document, paragraph);
    }

    [Fact]
    public void ListItemWithoutDirectIndent_TakesItsNumberingLevelIndent()
    {
        var (document, paragraph) = ListItem(new ParagraphFormatting
        {
            ListLevelIndentLeftPt = 36,
            ListLevelFirstLineIndentPt = -18,
        });

        var resolved = DocumentParagraphFormattingResolver.Resolve(document, paragraph);

        resolved.IndentLeftPt.Should().Be(36);
        resolved.FirstLineIndentPt.Should().Be(-18);
    }

    [Fact]
    public void DirectIndent_WinsOverTheNumberingLevelIndent()
    {
        var (document, paragraph) = ListItem(new ParagraphFormatting
        {
            IndentLeftPt = 72,
            IndentLeftIsSet = true,
            ListLevelIndentLeftPt = 36,
            ListLevelFirstLineIndentPt = -18,
        });

        var resolved = DocumentParagraphFormattingResolver.Resolve(document, paragraph);

        resolved.IndentLeftPt.Should().Be(72, "the paragraph's own w:ind/@w:left beats the level's");
        resolved.FirstLineIndentPt.Should().Be(-18, "the hanging is not set directly, so the level's applies");
    }

    [Fact]
    public void ParagraphWithoutLevelIndent_IsLeftAlone()
    {
        var (document, paragraph) = ListItem(new ParagraphFormatting { IndentLeftPt = 12, IndentLeftIsSet = true });

        DocumentParagraphFormattingResolver.Resolve(document, paragraph).IndentLeftPt.Should().Be(12);
    }

    private static Paragraph DeletedMark(params Run[] runs)
    {
        var paragraph = new Paragraph { MarkRevision = RevisionKind.Deleted };
        foreach (var run in runs)
            paragraph.Runs.Add(run);
        return paragraph;
    }

    [Fact]
    public void ParagraphWhoseMarkAndTextAreDeleted_CollapsesInTheFinalView()
    {
        var paragraph = DeletedMark(new Run("gone") { Revision = RevisionKind.Deleted });

        DocumentParagraphPaginationPlanner.IsFullyDeletedParagraphCollapsedInFinalView(paragraph, deletedTextVisible: false)
            .Should().BeTrue();
        DocumentParagraphPaginationPlanner.IsFullyDeletedParagraphCollapsedInFinalView(paragraph, deletedTextVisible: true)
            .Should().BeFalse("All Markup still shows the struck-through paragraph");
    }

    [Fact]
    public void ParagraphWithSurvivingText_DoesNotCollapse()
    {
        var paragraph = DeletedMark(
            new Run("gone") { Revision = RevisionKind.Deleted },
            new Run("kept"));

        DocumentParagraphPaginationPlanner.IsFullyDeletedParagraphCollapsedInFinalView(paragraph, deletedTextVisible: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ParagraphWithOrdinaryMark_DoesNotCollapse()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted });

        DocumentParagraphPaginationPlanner.IsFullyDeletedParagraphCollapsedInFinalView(paragraph, deletedTextVisible: false)
            .Should().BeFalse();
    }
}
