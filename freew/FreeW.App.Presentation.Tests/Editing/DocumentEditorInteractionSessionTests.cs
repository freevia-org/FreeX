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

    [Fact]
    public void FormatPainterAppliesRunAndParagraphFormattingAsOneUndoStep()
    {
        var targetRun = RunFormatting.Default with { Italic = true };
        var targetParagraph = ParagraphFormatting.Default with { Alignment = TextAlignment.Right };
        var first = ParagraphWith("alpha", targetRun, targetParagraph);
        var second = ParagraphWith("bravo", targetRun, targetParagraph);
        var session = SessionWith(first, second);
        var sourceRun = RunFormatting.Default with { Bold = true, FontFamily = "Cambria" };
        var sourceParagraph = ParagraphFormatting.Default with { Alignment = TextAlignment.Center };

        session.Interaction.ToggleFormatPainter(sourceRun, sourceParagraph).Should().BeTrue();
        session.Interaction.TryApplyFormatPainter(new DocumentTextRange(
            new DocumentTextPosition(0, 2),
            new DocumentTextPosition(1, 3))).Should().BeTrue();

        first.Runs.Should().Contain(run => run.Text == "pha" && run.Formatting == sourceRun);
        second.Runs.Should().Contain(run => run.Text == "bra" && run.Formatting == sourceRun);
        first.Formatting.Should().Be(sourceParagraph);
        second.Formatting.Should().Be(sourceParagraph);
        session.Interaction.IsFormatPainterArmed.Should().BeFalse();

        session.Commands.Undo();
        first.PlainText.Should().Be("alpha");
        second.PlainText.Should().Be("bravo");
        first.Runs.Should().OnlyContain(run => run.Formatting == targetRun);
        second.Runs.Should().OnlyContain(run => run.Formatting == targetRun);
        first.Formatting.Should().Be(targetParagraph);
        second.Formatting.Should().Be(targetParagraph);
    }

    [Fact]
    public void LockedFormatPainterSurvivesApplicationAndToggleCanDisarmIt()
    {
        var paragraph = ParagraphWith(
            "alpha",
            RunFormatting.Default,
            ParagraphFormatting.Default);
        var session = SessionWith(paragraph);

        session.Interaction.ToggleFormatPainter(
            RunFormatting.Default with { Bold = true },
            ParagraphFormatting.Default,
            locked: true).Should().BeTrue();
        session.Interaction.TryApplyFormatPainter(new DocumentTextRange(
            new DocumentTextPosition(0, 0),
            new DocumentTextPosition(0, 2))).Should().BeTrue();

        session.Interaction.IsFormatPainterArmed.Should().BeTrue();
        session.Interaction.ToggleFormatPainter(null, null).Should().BeFalse();
        session.Interaction.IsFormatPainterArmed.Should().BeFalse();
    }

    [Fact]
    public void CollapsedSelectionDoesNotConsumeFormatPainter()
    {
        var session = SessionWith(new Paragraph("alpha"));
        session.Interaction.ToggleFormatPainter(RunFormatting.Default, ParagraphFormatting.Default);
        var caret = new DocumentTextPosition(0, 2);

        session.Interaction.TryApplyFormatPainter(new DocumentTextRange(caret, caret)).Should().BeFalse();

        session.Interaction.IsFormatPainterArmed.Should().BeTrue();
    }

    private static DocumentEditingSession SessionWith(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static Paragraph ParagraphWith(
        string text,
        RunFormatting run,
        ParagraphFormatting paragraph)
    {
        var result = new Paragraph { Formatting = paragraph };
        result.Runs.Add(new Run(text, run));
        return result;
    }
}
