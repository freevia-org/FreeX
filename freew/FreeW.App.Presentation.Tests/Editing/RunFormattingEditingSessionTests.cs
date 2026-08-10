using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class RunFormattingEditingSessionTests
{
    [Fact]
    public void TryToggleRunFormatting_FormatsExactCrossParagraphRangesAsOneUndoStep()
    {
        var document = Document("alpha", "bravo");
        ((Paragraph)document.Blocks[1]).Runs[0].Formatting =
            RunFormatting.Default with { Bold = true };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryToggleRunFormatting(
            [Range(0, 2, 5), Range(1, 0, 2)],
            formatting => formatting.Bold,
            (formatting, value) => formatting with { Bold = value })
            .Should().BeTrue();

        BoldText((Paragraph)document.Blocks[0]).Should().Be("pha");
        BoldText((Paragraph)document.Blocks[1]).Should().Be("bravo");

        session.Commands.Undo().Should().BeTrue();
        BoldText((Paragraph)document.Blocks[0]).Should().BeEmpty();
        BoldText((Paragraph)document.Blocks[1]).Should().Be("bravo");
        session.Commands.CanUndo.Should().BeFalse();

        session.Commands.Redo().Should().BeTrue();
        BoldText((Paragraph)document.Blocks[0]).Should().Be("pha");
        BoldText((Paragraph)document.Blocks[1]).Should().Be("bravo");
    }

    [Fact]
    public void TryToggleRunFormatting_ClearsOnlyTheExactRangesWhenEveryRangeMatches()
    {
        var document = Document("alpha", "bravo");
        foreach (var paragraph in document.Blocks.Cast<Paragraph>())
            paragraph.Runs[0].Formatting = RunFormatting.Default with { Italic = true };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryToggleRunFormatting(
            [Range(0, 1, 4), Range(1, 0, 2)],
            formatting => formatting.Italic,
            (formatting, value) => formatting with { Italic = value })
            .Should().BeTrue();

        ItalicText((Paragraph)document.Blocks[0]).Should().Be("aa");
        ItalicText((Paragraph)document.Blocks[1]).Should().Be("avo");
    }

    [Fact]
    public void TrySetRunFormatting_PreservesMetadataAndCreatesTrackedFormattingRevision()
    {
        var document = Document("abcdef");
        document.TrackRevisions = true;
        document.DoNotTrackFormatting = false;
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("abcdef", RunFormatting.Default with { FontFamily = "Georgia" })
        {
            CommentId = "comment-1",
            HyperlinkUrl = "https://example.test/",
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Original author",
            RevisionDateXml = "2026-08-01T01:02:03Z",
        });
        var session = new DocumentEditingSession(
            () => "Ada",
            () => "2026-08-10T10:20:30Z");
        session.LoadDocument(document);

        session.TrySetRunFormatting(
            [Range(0, 1, 4)],
            formatting => formatting.Underline,
            formatting => formatting with { Underline = true },
            "Underline")
            .Should().BeTrue();

        var formatted = paragraph.Runs.Single(run => run.Text == "bcd");
        formatted.Formatting.Underline.Should().BeTrue();
        formatted.Formatting.FontFamily.Should().Be("Georgia");
        formatted.CommentId.Should().Be("comment-1");
        formatted.HyperlinkUrl.Should().Be("https://example.test/");
        formatted.Revision.Should().Be(RevisionKind.Inserted);
        formatted.RevisionAuthor.Should().Be("Original author");
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Ada");
        formatted.FormatRevision.DateXml.Should().Be("2026-08-10T10:20:30Z");
        formatted.FormatRevision.PreviousFormatting.FontFamily.Should().Be("Georgia");
    }

    [Fact]
    public void TrySetRunFormatting_SemanticNoOpIsHandledWithoutUndoEntry()
    {
        var document = Document("body");
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs[0].Formatting = RunFormatting.Default with { ColorHex = "#abcdef" };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TrySetRunFormatting(
            [Range(0, 0, 4)],
            formatting => string.Equals(
                formatting.ColorHex,
                "#ABCDEF",
                StringComparison.OrdinalIgnoreCase),
            formatting => formatting with { ColorHex = "#ABCDEF" })
            .Should().BeTrue();

        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Formatting.ColorHex.Should().Be("#abcdef");
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetMultiLevelNumberFormats_NormalizesAndUndoesAsOneFormattingEdit()
    {
        var document = Document("body");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.SetMultiLevelNumberFormats(
            [ListNumberFormat.UpperRoman, ListNumberFormat.LowerLetter])
            .Should().BeTrue();

        document.MultiLevelList.NumberFormats[0].Should().Be(ListNumberFormat.UpperRoman);
        document.MultiLevelList.NumberFormats[1].Should().Be(ListNumberFormat.LowerLetter);
        document.MultiLevelList.NumberFormats.Skip(2)
            .Should().OnlyContain(format => format == ListNumberFormat.Decimal);

        session.Commands.Undo().Should().BeTrue();
        document.MultiLevelList.NumberFormats
            .Should().OnlyContain(format => format == ListNumberFormat.Decimal);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PairedRenderersDelegateBodyRangeFormattingPolicyToPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var rendererSources = new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
        }.Select(File.ReadAllText).ToArray();

        foreach (var source in rendererSources)
        {
            source.Should().Contain("_editingSession.TrySetRunFormatting(");
            source.Should().Contain("_editingSession.TryToggleRunFormatting(");
            source.Should().Contain("_editingSession.FormatParagraphRuns(");
            source.Should().Contain("_editingSession.SetMultiLevelNumberFormats(");
            source.Should().NotContain("FormatRunRangeCommand");
            source.Should().NotContain("RunRangeAllMatches(");
            source.Should().NotContain("ApplyRunFormattingToTextRange(");
            source.Should().NotContain("WithTrackedRunFormatting(");
            source.Should().NotContain("CommitUndoGroup(\"Character Formatting\")");
            source.Should().NotContain("CommitUndoGroup(\"Proofing Language\")");
            source.Should().NotContain(".MultiLevelList.SetNumberFormats(");
        }
    }

    private static DocumentTextRange Range(int blockIndex, int startOffset, int endOffset) =>
        new(
            new DocumentTextPosition(blockIndex, startOffset),
            new DocumentTextPosition(blockIndex, endOffset));

    private static string BoldText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Where(run => run.Formatting.Bold).Select(run => run.Text));

    private static string ItalicText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Where(run => run.Formatting.Italic).Select(run => run.Text));

    private static TextDocument Document(params string[] texts)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in texts)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }
}
