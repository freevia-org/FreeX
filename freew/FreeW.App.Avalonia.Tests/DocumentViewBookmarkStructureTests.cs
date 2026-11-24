using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewBookmarkStructureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnterReplacesForwardOrReversedSelectionAndRestoresMetadataInOneUndo(bool reversed)
    {
        await Session.Dispatch(() =>
        {
            var source = FormattedBookmarkParagraph();
            var section = new Section(new PageSettings(), SectionBreakKind.Continuous);
            source.SectionBreak = section;
            var view = Load(source);
            view.SetBodySelectionForTest(0, reversed ? 5 : 2, 0, reversed ? 2 : 5);

            view.InsertParagraphBreakPublic();

            view.Document.Paragraphs.Select(p => p.PlainText).Should().Equal("ab", "f");
            var first = (Paragraph)view.Document.Blocks[0];
            var second = (Paragraph)view.Document.Blocks[1];
            first.BookmarkNames.Should().Equal("whole");
            second.BookmarkNames.Should().BeEmpty();
            first.SectionBreak.Should().BeNull();
            second.SectionBreak!.BreakKind.Should().Be(SectionBreakKind.Continuous);
            first.Runs[0].Formatting.Bold.Should().BeTrue();
            second.Runs[0].Formatting.Italic.Should().BeTrue();
            first.BookmarkBoundaries.Should().BeEmpty();
            second.BookmarkBoundaries.Select(b => b.Kind).Should().Equal(BookmarkBoundaryKind.Start, BookmarkBoundaryKind.End);
            second.BookmarkBoundaries.Select(b => BoundaryOffset(second, b)).Should().OnlyContain(offset => offset == 0);
            (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((1, 0));

            view.Undo();
            view.Document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(source);
            source.SectionBreak.Should().BeSameAs(section);
            source.PlainText.Should().Be("abcdef");
            view.SelectedText.Should().Be("cde");
            view.CanUndo.Should().BeFalse();
            view.Redo();
            view.Document.Paragraphs.Select(p => p.PlainText).Should().Equal("ab", "f");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EnterKeepsSpanningBookmarkAcrossFormattedSplitWithoutDuplicatingBoundaries()
    {
        await Session.Dispatch(() =>
        {
            var source = FormattedBookmarkParagraph();
            var view = Load(source);
            view.MoveCaretToBlockForTest(0, 3);
            view.InsertParagraphBreakPublic();
            var first = (Paragraph)view.Document.Blocks[0];
            var second = (Paragraph)view.Document.Blocks[1];
            first.PlainText.Should().Be("abc");
            second.PlainText.Should().Be("def");
            var start = first.BookmarkBoundaries.Should().ContainSingle().Subject;
            var end = second.BookmarkBoundaries.Should().ContainSingle().Subject;
            start.Kind.Should().Be(BookmarkBoundaryKind.Start);
            end.Kind.Should().Be(BookmarkBoundaryKind.End);
            start.PairKey.Should().Be(end.PairKey);
            BoundaryOffset(first, start).Should().Be(2);
            BoundaryOffset(second, end).Should().Be(1);
            view.Undo();
            view.Document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(source);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 1, 2, 1, 4)]
    [InlineData(2, 1, 0, 1, 2)]
    [InlineData(4, 0, 2, 0, 4)]
    [InlineData(6, 0, 2, 0, 4)]
    public async Task EnterRespectsStartAndEndAffinityAtTheSplit(int split, int startBlock, int startOffset,
        int endBlock, int endOffset)
    {
        await Session.Dispatch(() =>
        {
            var view = Load(FormattedBookmarkParagraph());
            view.MoveCaretToBlockForTest(0, split);
            view.InsertParagraphBreakPublic();
            var paragraphs = view.Document.Paragraphs.ToArray();
            paragraphs.Should().HaveCount(2);
            var boundaries = paragraphs.SelectMany((paragraph, block) => paragraph.BookmarkBoundaries.Select(boundary =>
                (boundary.Kind, Block: block, Offset: BoundaryOffset(paragraph, boundary)))).ToArray();
            boundaries.Should().HaveCount(2);
            boundaries.Should().Contain((BookmarkBoundaryKind.Start, startBlock, startOffset));
            boundaries.Should().Contain((BookmarkBoundaryKind.End, endBlock, endOffset));
            paragraphs[0].BookmarkNames.Should().Equal("whole");
            paragraphs[1].BookmarkNames.Should().BeEmpty();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParagraphBoundaryDeletionPreservesRunBookmarksAndTheLaterSection(bool backward)
    {
        await Session.Dispatch(() =>
        {
            var earlier = FormattedBookmarkParagraph();
            var later = FormattedBookmarkParagraph();
            earlier.SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage);
            later.SectionBreak = new Section(new PageSettings(), SectionBreakKind.Continuous);
            later.BookmarkNames.Clear();
            later.BookmarkNames.Add("later");
            later.BookmarkBoundaries.Clear();
            later.BookmarkBoundaries.Add(new("second", BookmarkBoundaryKind.Start, 1, "second"));
            later.BookmarkBoundaries.Add(new("second", BookmarkBoundaryKind.End, 2));
            var view = Load(earlier, later);
            view.MoveCaretToBlockForTest(backward ? 1 : 0, backward ? 0 : 6);

            if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

            var merged = view.Document.Paragraphs.Should().ContainSingle().Subject;
            merged.PlainText.Should().Be("abcdefabcdef");
            merged.BookmarkNames.Should().Equal("whole", "later");
            merged.BookmarkBoundaries.Select(b => BoundaryOffset(merged, b)).Should().Equal(2, 4, 8, 10);
            merged.SectionBreak!.BreakKind.Should().Be(SectionBreakKind.Continuous);
            merged.Runs.Where(run => run.Text == "ab").Should().OnlyContain(run => run.Formatting.Bold);
            (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((0, 6));
            view.Undo();
            view.Document.Blocks.Should().Equal(earlier, later);
            view.CanUndo.Should().BeFalse();
            view.Redo();
            view.Document.Paragraphs.Should().ContainSingle().Which.PlainText.Should().Be("abcdefabcdef");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("control")]
    [InlineData("comment")]
    [InlineData("annotation")]
    public async Task UnsupportedRichParagraphEnterRefusesWithoutLosingTextOrHistory(string kind)
    {
        await Session.Dispatch(() =>
        {
            var source = FormattedBookmarkParagraph();
            source.Runs.Add(kind switch
            {
                "field" => Run.DateField(cached: "cached"),
                "control" => Run.PlainTextControl("control"),
                "comment" => new Run("comment") { CommentId = 1 },
                _ => new Run(string.Empty) { CommentId = 1, IsCommentReference = true },
            });
            var original = source.PlainText;
            var view = Load(source);
            view.MoveCaretToBlockForTest(0, 1);
            view.InsertParagraphBreakPublic();
            view.Document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(source);
            source.PlainText.Should().Be(original);
            view.CanUndo.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackspaceNeverSkipsAnInterveningTableToReachABookmark()
    {
        await Session.Dispatch(() =>
        {
            var earlier = FormattedBookmarkParagraph();
            var table = Table.Create(1, 1);
            var later = FormattedBookmarkParagraph();
            var view = Load(earlier, table, later);
            view.MoveCaretToBlockForTest(2, 0);
            view.BackspaceForTest();
            view.Document.Blocks.Should().Equal(earlier, table, later);
            view.CanUndo.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParagraphBoundaryDeletionRefusesAnAtomicNeighbour(bool backward)
    {
        await Session.Dispatch(() =>
        {
            var earlier = FormattedBookmarkParagraph();
            var later = FormattedBookmarkParagraph();
            later.Runs.Add(Run.DateField(cached: "cached"));
            var view = Load(earlier, later);
            view.MoveCaretToBlockForTest(backward ? 1 : 0, backward ? 0 : 6);
            if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();
            view.Document.Blocks.Should().Equal(earlier, later);
            later.Runs.Should().Contain(run => run.FieldKind == RunFieldKind.Date);
            view.CanUndo.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TrackedBookmarkSplitRemainsRefusedUntilParagraphMarkRevisionsAreSupported()
    {
        await Session.Dispatch(() =>
        {
            var source = FormattedBookmarkParagraph();
            var view = Load(source);
            view.ToggleTrackChanges().Should().BeTrue();
            var couldUndoBeforeEnter = view.CanUndo;
            view.MoveCaretToBlockForTest(0, 3);
            view.InsertParagraphBreakPublic();
            view.Document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(source);
            source.PlainText.Should().Be("abcdef");
            view.CanUndo.Should().Be(couldUndoBeforeEnter);
        }, CancellationToken.None);
    }

    private static Paragraph FormattedBookmarkParagraph()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("ab", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(new Run("cd"));
        paragraph.Runs.Add(new Run("ef", RunFormatting.Default with { Italic = true }));
        paragraph.BookmarkNames.Add("whole");
        paragraph.BookmarkBoundaries.Add(new("span", BookmarkBoundaryKind.Start, 1, "span"));
        paragraph.BookmarkBoundaries.Add(new("span", BookmarkBoundaryKind.End, 2));
        return paragraph;
    }

    private static int BoundaryOffset(Paragraph paragraph, BookmarkBoundary boundary) =>
        paragraph.Runs.Take(boundary.RunIndex).Sum(run => run.Text.Length);

    private static DocumentView Load(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }
}
