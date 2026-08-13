using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentParagraphFormattingCoordinatorTests
{
    [Fact]
    public void AggregateTogglesApplyOneValueAcrossTargetsAndUndoAtomically()
    {
        var document = Document("alpha", "bravo");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        second.Formatting = second.Formatting with { KeepWithNext = true };
        var session = Session(document);

        session.Paragraphs.ToggleKeepWithNext([0, 1]).Should().BeTrue();

        first.Formatting.KeepWithNext.Should().BeTrue();
        second.Formatting.KeepWithNext.Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        first.Formatting.KeepWithNext.Should().BeFalse();
        second.Formatting.KeepWithNext.Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void BordersAndShadingUseSharedTargetFilteringAndCaseInsensitiveTogglePolicy()
    {
        var document = Document("alpha", "bravo");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        first.Formatting = first.Formatting with { ShadingColorHex = "#abcdef" };
        second.Formatting = second.Formatting with { ShadingColorHex = "#ABCDEF" };
        document.Blocks.Insert(1, new Table());
        var session = Session(document);

        session.Paragraphs.ToggleShading([0, 1, 2, 999], "#AbCdEf").Should().BeTrue();
        first.Formatting.ShadingColorHex.Should().BeNull();
        second.Formatting.ShadingColorHex.Should().BeNull();

        session.Paragraphs.ToggleBorder([0, 1, 2], "#123456", 1.25).Should().BeTrue();
        first.Formatting.Border.Should().Be(new ParagraphBorder("#123456", 1.25));
        second.Formatting.Border.Should().Be(new ParagraphBorder("#123456", 1.25));
    }

    [Fact]
    public void ParagraphDialogFormattingNormalizesValuesAndAuthorsExplicitFlags()
    {
        var document = Document("alpha", "bravo");
        var session = Session(document);

        session.Paragraphs.ApplyDialogFormatting(
                [0, 1],
                indentLeftPt: -4,
                indentRightPt: -5,
                firstLineIndentPt: -6,
                spaceBeforePt: -7,
                spaceAfterPt: -8,
                lineSpacing: 0.2,
                keepWithNext: true,
                keepLinesTogether: true,
                widowControl: false,
                pageBreakBefore: true,
                suppressAutoHyphens: true,
                suppressLineNumbers: true,
                contextualSpacing: true)
            .Should().BeTrue();

        foreach (var paragraph in document.Blocks.Cast<Paragraph>())
        {
            paragraph.Formatting.IndentLeftPt.Should().Be(0);
            paragraph.Formatting.IndentRightPt.Should().Be(0);
            paragraph.Formatting.FirstLineIndentPt.Should().Be(-6);
            paragraph.Formatting.SpaceBeforePt.Should().Be(0);
            paragraph.Formatting.SpaceAfterPt.Should().Be(0);
            paragraph.Formatting.SpaceBeforeIsSet.Should().BeTrue();
            paragraph.Formatting.SpaceAfterIsSet.Should().BeTrue();
            paragraph.Formatting.LineRule.Should().Be(LineSpacingRule.Multiple);
            paragraph.Formatting.LineSpacing.Should().Be(0.5);
            paragraph.Formatting.LineSpacingIsSet.Should().BeTrue();
            paragraph.Formatting.WidowControlIsSet.Should().BeTrue();
            paragraph.Formatting.SuppressAutoHyphensIsSet.Should().BeTrue();
            paragraph.Formatting.SuppressLineNumbersIsSet.Should().BeTrue();
            paragraph.Formatting.ContextualSpacing.Should().BeTrue();
        }

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting == ParagraphFormatting.Default);
    }

    [Fact]
    public void ProofingLanguagePlanFiltersInvalidAndRendererRejectedParagraphs()
    {
        var document = Document("alpha", "bravo");
        var session = Session(document);
        var plan = new ProofingLanguageApplyPlan(
            "fr-FR",
            [
                new ProofingLanguageTextRange(-1, 0, 1),
                new ProofingLanguageTextRange(0, 1, 4),
                new ProofingLanguageTextRange(1, 0, 2),
                new ProofingLanguageTextRange(99, 0, 1),
            ]);

        session.TryApplyProofingLanguage(plan, (index, _) => index == 0).Should().BeTrue();

        LanguageText((Paragraph)document.Blocks[0], "fr-FR").Should().Be("lph");
        LanguageText((Paragraph)document.Blocks[1], "fr-FR").Should().BeEmpty();
        session.Commands.Undo().Should().BeTrue();
        LanguageText((Paragraph)document.Blocks[0], "fr-FR").Should().BeEmpty();
    }

    private static DocumentEditingSession Session(TextDocument document)
    {
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static TextDocument Document(params string[] texts)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in texts)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static string LanguageText(Paragraph paragraph, string languageTag) =>
        string.Concat(paragraph.Runs
            .Where(run => string.Equals(run.Formatting.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
            .Select(run => run.Text));
}

public sealed class DocumentParagraphFormattingOwnershipTests
{
    [Fact]
    public void PairedRenderersDelegateSharedParagraphAndProofingPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sources = new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
        }.Select(File.ReadAllText).ToArray();

        foreach (var source in sources)
        {
            source.Should().Contain("DocumentParagraphFormattingCoordinator ParagraphEdits");
            source.Should().Contain("ParagraphEdits.ToggleKeepWithNext");
            source.Should().Contain("ParagraphEdits.ToggleKeepLinesTogether");
            source.Should().Contain("ParagraphEdits.ToggleWidowControl");
            source.Should().Contain("ParagraphEdits.ToggleBorder");
            source.Should().Contain("ParagraphEdits.ToggleShading");
            source.Should().Contain("ParagraphEdits.SetTabStops");
            source.Should().Contain("ParagraphEdits.ApplyDialogFormatting");
            source.Should().Contain("_editingSession.TryApplyProofingLanguage(");
            source.Should().NotContain("var ranges = plan.Ranges");
            source.Should().NotContain("formatting => formatting with { LanguageTag = plan.LanguageTag }");
            source.Should().NotContain(".Any(p => !p.Formatting.KeepWithNext)");
            source.Should().NotContain(".Any(p => !p.Formatting.KeepLinesTogether)");
            source.Should().NotContain(".Any(p => !p.Formatting.WidowControl)");
        }
    }
}
