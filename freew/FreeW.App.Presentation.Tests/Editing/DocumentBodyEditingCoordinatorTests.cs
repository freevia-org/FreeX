using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentBodyEditingCoordinatorTests
{
    [Fact]
    public void TextInput_InterpretsCollapsedAndReversedSelectionsAndKeepsOneUndoEntry()
    {
        var session = SessionWith("abcdef");

        session.Body.TryApplyTextInput(
                Range(0, 2, 0, 2),
                new DocumentBodyTextInput("X", TrackChanges: false),
                out var inserted)
            .Should().BeTrue();

        inserted.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 3),
            DocumentBodyEditorTransition.InsertText));
        ParagraphAt(session, 0).PlainText.Should().Be("abXcdef");
        session.Commands.Undo().Should().BeTrue();

        session.Body.TryApplyTextInput(
                Range(0, 5, 0, 2),
                new DocumentBodyTextInput("Z", TrackChanges: false),
                out var replaced)
            .Should().BeTrue();

        replaced.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 3),
            DocumentBodyEditorTransition.ReplaceSelection));
        ParagraphAt(session, 0).PlainText.Should().Be("abZf");
        session.Commands.Undo().Should().BeTrue();
        ParagraphAt(session, 0).PlainText.Should().Be("abcdef");
    }

    [Fact]
    public void TextInput_RespectsAnExplicitRendererHyperlinkBoundaryDecision()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link") { HyperlinkUrl = "https://example.test" });
        var session = SessionWith(paragraph);

        session.Body.TryApplyTextInput(
                Range(0, 2, 0, 2),
                new DocumentBodyTextInput(
                    "X",
                    TrackChanges: false,
                    Formatting: paragraph.Runs[0].Formatting,
                    InheritHyperlink: false,
                    Hyperlink: null),
                out _)
            .Should().BeTrue();

        paragraph.PlainText.Should().Be("liXnk");
        paragraph.Runs.Should().Contain(run => run.Text == "X" && run.HyperlinkUrl == null);
    }

    [Fact]
    public void Deletion_UsesSelectionSemanticsBeforeDirection()
    {
        var session = SessionWith("abcdef");

        session.Body.TryApplyDeletion(
                Range(0, 5, 0, 2),
                DocumentBodyDeleteDirection.Forward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue();

        result.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 2),
            DocumentBodyEditorTransition.DeleteSelection));
        ParagraphAt(session, 0).PlainText.Should().Be("abf");
        session.Commands.Undo().Should().BeTrue();
        ParagraphAt(session, 0).PlainText.Should().Be("abcdef");
    }

    [Fact]
    public void Deletion_ReportsCharacterAndParagraphBoundaryTransitions()
    {
        var session = SessionWith("alpha", "beta");

        session.Body.TryApplyDeletion(
                Range(1, 0, 1, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var backwardMerge)
            .Should().BeTrue();

        backwardMerge.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 5),
            DocumentBodyEditorTransition.MergeWithPreviousParagraph));
        ParagraphTexts(session).Should().Equal("alphabeta");
        session.Commands.Undo().Should().BeTrue();

        session.Body.TryApplyDeletion(
                Range(0, 5, 0, 5),
                DocumentBodyDeleteDirection.Forward,
                trackChanges: false,
                mergeForwardBoundary: true,
                out var forwardMerge)
            .Should().BeTrue();

        forwardMerge.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 5),
            DocumentBodyEditorTransition.MergeWithNextParagraph));
        ParagraphTexts(session).Should().Equal("alphabeta");
    }

    [Fact]
    public void TrackedForwardDelete_AdvancesPastRetainedDeletedText()
    {
        var session = new DocumentEditingSession(
            () => "Ada",
            () => "2026-08-06T10:20:30Z");
        session.LoadDocument(DocumentWith("abc"));

        session.Body.TryApplyDeletion(
                Range(0, 0, 0, 0),
                DocumentBodyDeleteDirection.Forward,
                trackChanges: true,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue();

        result.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 1),
            DocumentBodyEditorTransition.DeleteCharacterForward));
        ParagraphAt(session, 0).Runs.Should().Contain(run =>
            run.Text == "a"
            && run.Revision == RevisionKind.Deleted
            && run.RevisionAuthor == "Ada");
    }

    [Theory]
    [InlineData(2, ListKind.Bullet, 1)]
    [InlineData(0, ListKind.None, 0)]
    public void BackspaceAtListStart_OutdentsOrExitsTheList(
        int initialLevel,
        ListKind expectedKind,
        int expectedLevel)
    {
        var paragraph = new Paragraph("item")
        {
            Formatting = new ParagraphFormatting
            {
                ListKind = ListKind.Bullet,
                ListLevel = initialLevel,
            },
        };
        var session = SessionWith(paragraph);

        session.Body.TryApplyDeletion(
                Range(0, 0, 0, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue();

        result.Transition.Should().Be(DocumentBodyEditorTransition.OutdentListItem);
        ParagraphAt(session, 0).Formatting.ListKind.Should().Be(expectedKind);
        ParagraphAt(session, 0).Formatting.ListLevel.Should().Be(expectedLevel);
        session.Commands.Undo().Should().BeTrue();
        ParagraphAt(session, 0).Formatting.ListKind.Should().Be(ListKind.Bullet);
        ParagraphAt(session, 0).Formatting.ListLevel.Should().Be(initialLevel);
    }

    [Fact]
    public void ParagraphBreak_ReportsSplitAndEmptyListExitTransitions()
    {
        var listed = new Paragraph("item")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Number },
        };
        var session = SessionWith(listed);

        session.Body.TryApplyParagraphBreak(Range(0, 2, 0, 2), out var split)
            .Should().BeTrue();

        split.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(1, 0),
            DocumentBodyEditorTransition.InsertParagraphBreak));
        ParagraphTexts(session).Should().Equal("it", "em");
        ParagraphAt(session, 1).Formatting.ListKind.Should().Be(ListKind.Number);

        var empty = new Paragraph
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 1 },
        };
        session = SessionWith(empty);

        session.Body.TryApplyParagraphBreak(Range(0, 0, 0, 0), out var exited)
            .Should().BeTrue();

        exited.Should().Be(new DocumentBodyEditorActionResult(
            new DocumentTextPosition(0, 0),
            DocumentBodyEditorTransition.ExitEmptyList));
        session.Document.Blocks.Should().HaveCount(1);
        ParagraphAt(session, 0).Formatting.ListKind.Should().Be(ListKind.None);
    }

    [Fact]
    public void StructurallySpecialParagraphs_AreLeftForRendererFallback()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("abc") { IsPageBreak = true });
        var session = SessionWith(paragraph);

        session.Body.TryApplyDeletion(
                Range(0, 1, 0, 1),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out _)
            .Should().BeFalse();
        session.Body.TryApplyParagraphBreak(Range(0, 1, 0, 1), out _)
            .Should().BeFalse();

        paragraph.PlainText.Should().Be("abc");
        session.Commands.CanUndo.Should().BeFalse();
    }

    private static DocumentTextRange Range(
        int anchorBlock,
        int anchorOffset,
        int activeBlock,
        int activeOffset) => new(
        new DocumentTextPosition(anchorBlock, anchorOffset),
        new DocumentTextPosition(activeBlock, activeOffset));

    private static DocumentEditingSession SessionWith(params string[] paragraphs) =>
        SessionWith(DocumentWith(paragraphs));

    private static DocumentEditingSession SessionWith(params Paragraph[] paragraphs)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(paragraphs);
        return SessionWith(document);
    }

    private static DocumentEditingSession SessionWith(TextDocument document)
    {
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static TextDocument DocumentWith(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static Paragraph ParagraphAt(DocumentEditingSession session, int index) =>
        (Paragraph)session.Document.Blocks[index];

    private static IEnumerable<string> ParagraphTexts(DocumentEditingSession session) =>
        session.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText);
}
