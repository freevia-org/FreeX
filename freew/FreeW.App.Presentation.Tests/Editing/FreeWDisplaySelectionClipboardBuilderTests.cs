using Free.Shared.AppServices;
using FreeW.App.Presentation.Editing;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class FreeWDisplaySelectionClipboardBuilderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("old")]
    [InlineData("a long cached field result")]
    public void LiteralSuffixAfterAFieldCopiesTheDisplayedSliceRatherThanTheCachedOffset(string cached)
    {
        var field = Run.DateField(cached);
        var literal = new Run(" suffix", RunFormatting.Default with { Bold = true });
        var (source, paragraph) = Document(field, literal);
        var plan = DocumentDisplaySelectionPlanner.Plan(
            [new(0, field, "2026-11-24", true), new(1, literal, literal.Text, false)], 11, 14);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.RichDocument!.PlainText.Should().Be("suf");
        result.NativeDocument!.PlainText.Should().Be("suf");
        result.NativeDocument.Paragraphs.Single().Runs.Single().FieldKind.Should().Be(RunFieldKind.None);
        result.RichDocument.Paragraphs.Single().Runs.Single().Formatting.Bold.Should().BeTrue();
        field.Text.Should().Be(cached);
        literal.Text.Should().Be(" suffix");
    }

    [Theory]
    [InlineData("date")]
    [InlineData("footnote")]
    [InlineData("endnote")]
    [InlineData("equation")]
    public void WholeZeroCacheAtomKeepsItsPayloadAndLiteralNeighbors(string kind)
    {
        var atom = kind switch
        {
            "date" => Run.DateField(),
            "footnote" => Run.FootnoteReference(403),
            "endnote" => Run.EndnoteReference(404),
            _ => new Run("") { Equation = new Equation() },
        };
        atom.Text = "";
        var left = new Run("left");
        var right = new Run("right");
        var (source, paragraph) = Document(left, atom, right);
        source.Footnotes[403] = new Footnote(403, "selected note");
        source.Footnotes[999] = new Footnote(999, "unselected note");
        source.Endnotes[404] = new Endnote(404, "selected endnote");
        var plan = DocumentDisplaySelectionPlanner.Plan(
            [new(0, left, "left", false), new(1, atom, "DISPLAY", true), new(2, right, "right", false)], 0, 16);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.RichDocument!.PlainText.Should().Be("leftDISPLAYright");
        result.NativeDocument!.Paragraphs.Single().Runs.Should().HaveCount(3);
        var copied = result.NativeDocument.Paragraphs.Single().Runs[1];
        copied.Should().NotBeSameAs(atom);
        copied.Text.Should().BeEmpty();
        copied.FieldKind.Should().Be(atom.FieldKind);
        copied.FootnoteId.Should().Be(atom.FootnoteId);
        copied.EndnoteId.Should().Be(atom.EndnoteId);
        (copied.Equation is not null).Should().Be(kind == "equation");
        result.NativeDocument.Footnotes.Keys.Should().Equal(kind == "footnote" ? [403] : Array.Empty<int>());
        result.NativeDocument.Endnotes.Keys.Should().Equal(kind == "endnote" ? [404] : Array.Empty<int>());
        paragraph.Runs.Should().Equal(left, atom, right);
    }

    [Fact]
    public void LiteralNativeSliceKeepsMarksAndStyleWhileRichFormattingIsResolved()
    {
        var run = new Run("abcdef")
        {
            StyleId = "Emphasis", CommentId = 72, Revision = RevisionKind.Inserted,
            RevisionAuthor = "Review author", HyperlinkUrl = "https://example.invalid/reference",
        };
        var (source, paragraph) = Document(run);
        source.DefaultRun = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 14 };
        source.Styles["Emphasis"] = new DocumentStyle
        {
            Id = "Emphasis", Name = "Emphasis", Run = RunFormatting.Default with { Bold = true },
        };
        var root = new Comment(71, "root");
        root.AddReply(72, "reply");
        source.Comments[71] = root;
        var plan = DocumentDisplaySelectionPlanner.Plan([new(0, run, run.Text, false)], 1, 4);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        var rich = result.RichDocument!.Paragraphs.Single().Runs.Single();
        rich.Text.Should().Be("bcd");
        rich.Formatting.Bold.Should().BeTrue();
        rich.Formatting.FontFamily.Should().Be("Arial");
        var native = result.NativeDocument!.Paragraphs.Single().Runs.Single();
        native.Text.Should().Be("bcd");
        native.StyleId.Should().Be("Emphasis");
        native.CommentId.Should().Be(72);
        native.Revision.Should().Be(RevisionKind.Inserted);
        native.RevisionAuthor.Should().Be("Review author");
        native.HyperlinkUrl.Should().Be(run.HyperlinkUrl);
        result.NativeDocument.Comments.Keys.Should().Equal(71);
        result.NativeDocument.Comments[71].Replies.Single().Id.Should().Be(72);
        result.NativeDocument.Styles.Should().ContainKey("Emphasis");
        run.Text.Should().Be("abcdef");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 4)]
    [InlineData(1, 3)]
    public void PartialAtomicSelectionCopiesExactDisplayWithoutANativeField(int anchor, int active)
    {
        var field = Run.DateField("cached instruction result");
        var (source, paragraph) = Document(field);
        var plan = DocumentDisplaySelectionPlanner.Plan([new(0, field, "DATE", true)], anchor, active);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.RichDocument!.PlainText.Should().Be("DATE"[anchor..active]);
        result.RichDocument.Paragraphs.Single().Runs.Single().FieldKind.Should().Be(RunFieldKind.None);
        result.NativeDocument.Should().BeNull();
        field.Text.Should().Be("cached instruction result");
    }

    [Fact]
    public void CrLfUsesDisplayedTextForRichAndTheMappedSourceRangeForNative()
    {
        var run = new Run("a\r\nb");
        var (source, paragraph) = Document(run);
        var plan = DocumentDisplaySelectionPlanner.Plan(
            [new(0, run, "a\nb", false, [0, 1, 3, 4])], 1, 2);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.RichDocument!.PlainText.Should().Be("\n");
        result.NativeDocument!.PlainText.Should().Be("\r\n");
    }

    [Fact]
    public void NativeNoteAndNestedCommentThreadSurviveActualClipboardSerialization()
    {
        var note = Run.FootnoteReference(403);
        note.Text = "";
        var (source, paragraph) = Document(note);
        var content = new Paragraph("note body");
        content.Runs[0].CommentId = 72;
        source.Footnotes[403] = new Footnote(403);
        source.Footnotes[403].Content.Add(content);
        var thread = new Comment(71, "root");
        thread.AddReply(72, "reply");
        source.Comments[71] = thread;
        var plan = DocumentDisplaySelectionPlanner.Plan([new(0, note, "1", true)], 0, 1);
        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        var clipboard = FreeWClipboardApplicationWorkflow.CreateWriteContent(plan.SelectedText, result.RichDocument, result.NativeDocument)!;
        var bytes = clipboard.GetBytes(FreeWClipboardApplicationWorkflow.NativeDocumentFormat, PlatformClipboardFormatScope.Application);
        bytes.Should().NotBeNullOrEmpty();
        var reopened = DocxReader.Read(new MemoryStream(bytes!));

        reopened.Paragraphs.Single().Runs.Single().FootnoteId.Should().Be(403);
        reopened.Footnotes[403].PlainText.Should().Be("note body");
        reopened.Comments[71].Replies.Single().Id.Should().Be(72);
    }

    [Fact]
    public void BookmarksOutsideTheCopiedRunsAreDroppedAndOverlapsAreRemapped()
    {
        var prefix = new Run("prefix");
        var suffix = new Run("suffix");
        var (source, paragraph) = Document(prefix, suffix);
        paragraph.BookmarkBoundaries.Add(new("outside", BookmarkBoundaryKind.Start, 0, "Outside"));
        paragraph.BookmarkBoundaries.Add(new("outside", BookmarkBoundaryKind.End, 1));
        paragraph.BookmarkBoundaries.Add(new("overlap", BookmarkBoundaryKind.Start, 0, "Overlap"));
        paragraph.BookmarkBoundaries.Add(new("overlap", BookmarkBoundaryKind.End, 2));
        var plan = DocumentDisplaySelectionPlanner.Plan(
            [new(0, prefix, prefix.Text, false), new(1, suffix, suffix.Text, false)], 7, 10);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.NativeDocument!.Paragraphs.Single().BookmarkBoundaries.Select(boundary => boundary.PairKey)
            .Should().Equal("overlap", "overlap");
        result.NativeDocument.Paragraphs.Single().BookmarkBoundaries.Select(boundary => boundary.RunIndex)
            .Should().Equal(0, 1);
    }

    [Fact]
    public void CollapsedSelectionDoesNotManufactureClipboardDocuments()
    {
        var run = new Run("text");
        var (source, paragraph) = Document(run);
        var plan = DocumentDisplaySelectionPlanner.Plan([new(0, run, run.Text, false)], 2, 2);

        var result = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);

        result.RichDocument.Should().BeNull();
        result.NativeDocument.Should().BeNull();
    }

    private static (TextDocument Document, Paragraph Paragraph) Document(params Run[] runs)
    {
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        source.Blocks.Add(paragraph);
        return (source, paragraph);
    }
}
