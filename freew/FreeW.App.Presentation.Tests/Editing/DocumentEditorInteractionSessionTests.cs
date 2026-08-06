using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentEditorInteractionSessionTests
{
    [Fact]
    public void SectionPositionCountsBreaksBeforeCaretAndHandlesUnknownCaret()
    {
        var session = SessionWith(
            new Paragraph("First") { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) },
            new Paragraph("Second"),
            new Paragraph("Third"));

        session.Interaction.SectionPosition(0).Should().Be(new DocumentSectionPosition(1, 2));
        session.Interaction.SectionPosition(1).Should().Be(new DocumentSectionPosition(2, 2));
        session.Interaction.SectionPosition(-1).Should().Be(new DocumentSectionPosition(2, 2));
    }

    [Fact]
    public void SelectionProjectionClipsFirstAndLastParagraphs()
    {
        var session = SessionWith(
            new Paragraph("alpha"),
            new Paragraph("bravo"),
            new Paragraph("charlie"));

        var text = session.Interaction.ProjectSelectionText(new DocumentTextRange(
            new DocumentTextPosition(0, 2),
            new DocumentTextPosition(2, 4)));

        text.Should().Be("pha\nbravo\nchar");
    }

    [Theory]
    [InlineData(DocumentPasteTextKind.TextOnly, "Paste Text Only")]
    [InlineData(DocumentPasteTextKind.MergeFormatting, "Merge Formatting")]
    public void PastePlanNormalizesTextAndPreservesParagraphBoundaries(
        DocumentPasteTextKind kind,
        string expectedLabel)
    {
        var session = new DocumentEditingSession();

        var plan = session.Interaction.PlanPasteText("one\r\ntwo\0", kind);

        plan.Text.Should().Be("one\ntwo");
        plan.Lines.Should().Equal("one", "two");
        plan.UndoLabel.Should().Be(expectedLabel);
    }

    [Theory]
    [InlineData(DocumentEditorInputKey.Z, DocumentEditorInputModifiers.Control, DocumentEditorInputIntent.Undo, false, true)]
    [InlineData(DocumentEditorInputKey.B, DocumentEditorInputModifiers.Control, DocumentEditorInputIntent.ToggleBold, false, true)]
    [InlineData(DocumentEditorInputKey.Left, DocumentEditorInputModifiers.Shift, DocumentEditorInputIntent.MovePrevious, true, false)]
    [InlineData(DocumentEditorInputKey.Tab, DocumentEditorInputModifiers.Shift, DocumentEditorInputIntent.NavigateTab, true, true)]
    [InlineData(DocumentEditorInputKey.Enter, DocumentEditorInputModifiers.None, DocumentEditorInputIntent.InsertParagraphBreak, false, true)]
    public void BodyKeyPlanProjectsPortableIntent(
        DocumentEditorInputKey key,
        DocumentEditorInputModifiers modifiers,
        DocumentEditorInputIntent intent,
        bool extendsSelection,
        bool isMutation)
    {
        var plan = DocumentEditorInteractionSession.PlanBodyKey(key, modifiers);

        plan.Intent.Should().Be(intent);
        plan.ExtendSelection.Should().Be(extendsSelection);
        plan.IsEditingMutation.Should().Be(isMutation);
    }

    private static DocumentEditingSession SessionWith(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }
}
