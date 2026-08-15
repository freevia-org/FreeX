using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class NamedStyleEditingSessionTests
{
    [Fact]
    public void ParagraphStylePreviewSwitchesAgainstOneBaselineAndCancelsWithoutUndo()
    {
        var document = LinkedDocument("first", "second", "third");
        document.Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            Type = StyleType.Paragraph,
        };
        ((Paragraph)document.Blocks[0]).StyleId = "Quote";
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var target = new NamedStyleApplicationTarget(
            [Range(0, 0, 5), Range(1, 0, 6)],
            [0, 1],
            HasTextSelection: true);

        session.ParagraphStylePreview.Preview("Heading1", target).Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.StyleId)
            .Should().Equal("Heading1", "Heading1", null);

        session.ParagraphStylePreview.Preview("Quote", Target(false, Range(2, 0, 5)))
            .Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.StyleId)
            .Should().Equal(
                ["Quote", "Quote", null],
                "later hovers must reuse the original selection and baseline");

        var cancelledTarget = session.ParagraphStylePreview.Cancel();

        cancelledTarget.Should().BeEquivalentTo(target);
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.StyleId)
            .Should().Equal("Quote", null, null);
        session.ParagraphStylePreview.HasActivePreview.Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ParagraphStylePreviewCommitRestoresBaselineAndUsesCapturedLinkedStyleTarget()
    {
        var document = LinkedDocument("alpha", "bravo");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var target = Target(hasTextSelection: true, Range(0, 1, 4));

        session.ParagraphStylePreview.Preview("Heading1", target).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading1");

        var committed = session.ParagraphStylePreview.Commit("Heading1");

        committed.Should().NotBeNull();
        committed!.Target.Should().BeEquivalentTo(target);
        committed.Application.Should().NotBeNull();
        committed.Application!.Kind.Should().Be(NamedStyleApplicationKind.Character);
        ((Paragraph)document.Blocks[0]).StyleId.Should().BeNull(
            "the linked character side must commit after restoring the paragraph preview");
        StyledText((Paragraph)document.Blocks[0]).Should().Be("lph");
        session.Commands.Undo().Should().BeTrue();
        StyledText((Paragraph)document.Blocks[0]).Should().BeEmpty();
    }

    [Fact]
    public void LoadingAnotherDocumentCancelsParagraphStylePreviewBeforeReplacement()
    {
        var original = LinkedDocument("original");
        var replacement = LinkedDocument("replacement");
        var session = new DocumentEditingSession();
        session.LoadDocument(original);
        session.ParagraphStylePreview.Preview(
            "Heading1",
            Target(hasTextSelection: false, Range(0, 0, 0))).Should().BeTrue();

        session.LoadDocument(replacement);

        ((Paragraph)original.Blocks[0]).StyleId.Should().BeNull();
        ((Paragraph)replacement.Blocks[0]).StyleId.Should().BeNull();
        session.ParagraphStylePreview.HasActivePreview.Should().BeFalse();
    }

    [Fact]
    public void ApplyNamedStyle_LinkedStyleFormatsExactCrossParagraphRangesAsOneUndoStep()
    {
        var document = LinkedDocument("alpha", "bravo");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.ApplyNamedStyle(
            "Heading1",
            Target(
                hasTextSelection: true,
                Range(0, 2, 5),
                Range(1, 0, 2)));

        result.Should().NotBeNull();
        result!.Kind.Should().Be(NamedStyleApplicationKind.Character);
        result.ModelChanged.Should().BeTrue();
        result.RequiresRendererProjection.Should().BeFalse();
        StyledText((Paragraph)document.Blocks[0]).Should().Be("pha");
        StyledText((Paragraph)document.Blocks[1]).Should().Be("br");
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == null);

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => !run.Formatting.Bold);
        session.Commands.CanUndo.Should().BeFalse();

        session.Commands.Redo().Should().BeTrue();
        StyledText((Paragraph)document.Blocks[0]).Should().Be("pha");
        StyledText((Paragraph)document.Blocks[1]).Should().Be("br");
    }

    [Fact]
    public void ApplyNamedStyle_CharacterRangePreservesRunMetadataAndCreatesFormattingRevision()
    {
        var document = LinkedDocument("abcdef");
        document.TrackRevisions = true;
        document.DoNotTrackFormatting = false;
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("abcdef", RunFormatting.Default with { FontFamily = "Georgia" })
        {
            CommentId = 42,
            HyperlinkUrl = "https://example.test/",
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Original author",
            RevisionDateXml = "2026-08-01T01:02:03Z",
        });
        var session = new DocumentEditingSession(
            () => "Ada",
            () => "2026-08-10T10:20:30Z");
        session.LoadDocument(document);

        session.ApplyNamedStyle(
            "Heading1Char",
            Target(hasTextSelection: true, Range(0, 1, 4)));

        var styled = paragraph.Runs.Single(run => run.Text == "bcd");
        styled.Formatting.Bold.Should().BeTrue();
        styled.Formatting.FontFamily.Should().Be("Georgia");
        styled.CommentId.Should().Be(42);
        styled.HyperlinkUrl.Should().Be("https://example.test/");
        styled.Revision.Should().Be(RevisionKind.Inserted);
        styled.RevisionAuthor.Should().Be("Original author");
        styled.FormatRevision.Should().NotBeNull();
        styled.FormatRevision!.Author.Should().Be("Ada");
        styled.FormatRevision.DateXml.Should().Be("2026-08-10T10:20:30Z");
        styled.FormatRevision.PreviousFormatting.FontFamily.Should().Be("Georgia");

        session.Commands.Undo().Should().BeTrue();
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Text.Should().Be("abcdef");
        paragraph.Runs[0].FormatRevision.Should().BeNull();
    }

    [Fact]
    public void ApplyNamedStyle_CollapsedCharacterStyleReturnsNativeProjectionWithoutMutatingModel()
    {
        var document = LinkedDocument("body");
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs[0].Formatting = RunFormatting.Default with
        {
            FontFamily = "Georgia",
            ColorHex = "#123456",
        };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.ApplyNamedStyle(
            "Heading1Char",
            Target(hasTextSelection: false, Range(0, 2, 2)));

        result.Should().NotBeNull();
        result!.ModelChanged.Should().BeFalse();
        result.RequiresRendererProjection.Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Formatting.Bold.Should().BeFalse();

        var projected = result.ProjectCharacterFormatting(paragraph.Runs[0].Formatting);
        projected.Bold.Should().BeTrue();
        projected.ColorHex.Should().Be("#2F5496");
        projected.FontFamily.Should().Be("Georgia");
    }

    [Fact]
    public void ApplyNamedStyle_CollapsedLinkedStyleAppliesParagraphSideAndUndo()
    {
        var document = LinkedDocument("first", "second");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.ApplyNamedStyle(
            "Heading1",
            new NamedStyleApplicationTarget(
                [Range(1, 3, 3)],
                [1],
                HasTextSelection: false));

        result!.Kind.Should().Be(NamedStyleApplicationKind.Paragraph);
        ((Paragraph)document.Blocks[0]).StyleId.Should().BeNull();
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("Heading1");
        session.Commands.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[1]).StyleId.Should().BeNull();
    }

    [Fact]
    public void ApplyNamedStyle_DisallowedCharacterFormattingDoesNotMutateOrRequestProjection()
    {
        var document = LinkedDocument("body");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.ApplyNamedStyle(
            "Heading1Char",
            new NamedStyleApplicationTarget(
                [Range(0, 0, 4)],
                [0],
                HasTextSelection: true,
                CanApplyCharacterFormatting: false));

        result!.ModelChanged.Should().BeFalse();
        result.RequiresRendererProjection.Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
        ((Paragraph)document.Blocks[0]).Runs.Should().OnlyContain(run => !run.Formatting.Bold);
    }

    [Fact]
    public void NamedStyleResolutionAndBodyMutationRemainOwnedByPresentationSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var rendererSources = new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
        }.Select(File.ReadAllText).ToArray();

        rendererSources.Should().OnlyContain(source => source.Contains(
            "_editingSession.ApplyNamedStyle(",
            StringComparison.Ordinal));
        rendererSources.Should().OnlyContain(source => !source.Contains(
            "NamedStyleApplicationPlanner.",
            StringComparison.Ordinal));
        rendererSources.Should().OnlyContain(source => !source.Contains(
            "CommitUndoGroup(\"Apply Character Style\")",
            StringComparison.Ordinal));
        rendererSources.Should().OnlyContain(source => source.Contains(
            "_editingSession.ParagraphStylePreview",
            StringComparison.Ordinal));
        rendererSources.Should().OnlyContain(source => !source.Contains(
            "_styleStyleIdSnapshot",
            StringComparison.Ordinal));
    }

    private static NamedStyleApplicationTarget Target(
        bool hasTextSelection,
        params DocumentTextRange[] ranges) =>
        new(
            ranges,
            ranges.Select(range => range.Start.BlockIndex).Distinct().ToArray(),
            hasTextSelection);

    private static DocumentTextRange Range(int blockIndex, int startOffset, int endOffset) =>
        new(
            new DocumentTextPosition(blockIndex, startOffset),
            new DocumentTextPosition(blockIndex, endOffset));

    private static string StyledText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs
            .Where(run => run.Formatting.Bold && run.Formatting.ColorHex == "#2F5496")
            .Select(run => run.Text));

    private static TextDocument LinkedDocument(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Type = StyleType.Paragraph,
            LinkedStyleId = "Heading1Char",
        };
        document.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
            Run = RunFormatting.Default with { Bold = true, ColorHex = "#2F5496" },
        };
        return document;
    }
}
