using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintCommentSummaryPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    private static CellAddress Address(uint row, uint col) => new(SheetId, row, col);

    [Fact]
    public void BuildEntries_MergesNotesAndThreadedCommentsInCellOrder()
    {
        var a1 = Address(1, 1);
        var b2 = Address(2, 2);
        var a3 = Address(3, 1);
        var comments = new Dictionary<CellAddress, string>
        {
            [a3] = "Later note",
            [b2] = "Plain note and thread"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [a1] = new("Review total", "Quinn"),
            [b2] = new("Thread duplicate", "FreeX")
        };

        var entries = PrintCommentSummaryPlanner.BuildEntries(comments, threadedComments);

        entries.Select(entry => entry.Address).Should().Equal(a1, b2, a3);
        entries.Select(entry => entry.Text).Should().Equal(
            "Quinn: Review total",
            "Note: Plain note and thread" + Environment.NewLine + "FreeX: Thread duplicate",
            "Later note");
    }

    [Fact]
    public void BuildPages_PaginatesAllEntriesUsingSummaryBodyHeight()
    {
        var comments = Enumerable.Range(1, 90)
            .ToDictionary(
                row => Address((uint)row, 1),
                row => $"Comment {row}");

        var pages = PrintCommentSummaryPlanner.BuildPages(
            comments,
            new Dictionary<CellAddress, ThreadedComment>(),
            pageHeight: 11 * PagePaginationPlanner.Dpi,
            marginTop: 0.75 * PagePaginationPlanner.Dpi);

        pages.SelectMany(page => page.Entries)
            .Select(entry => entry.Address.Row)
            .Should()
            .Equal(Enumerable.Range(1, 90).Select(row => (uint)row));
        pages.Count.Should().BeGreaterThan(1);
        pages.Select(page => page.PageIndex).Should().Equal(Enumerable.Range(0, pages.Count));
    }

    [Fact]
    public void BuildPages_ReturnsEmptyWhenNoPrintableCommentsExist()
    {
        PrintCommentSummaryPlanner.BuildPages(
                new Dictionary<CellAddress, string>(),
                new Dictionary<CellAddress, ThreadedComment>(),
                pageHeight: 100,
                marginTop: 10)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void WrapOverlayText_BoundsLongTextToMaxLinesAndEllipsis()
    {
        var lines = PrintCommentSummaryPlanner.WrapOverlayText(
            "alpha beta gamma delta hidden-tail-token",
            maxWidth: 10,
            measureWidth: text => text.Length,
            maxLines: 2);

        lines.Should().Equal("alpha beta", "gamma" + PrintCommentSummaryPlanner.Ellipsis);
    }

    [Fact]
    public void WrapOverlayText_RespectsHardLinesAndTruncatesFinalLine()
    {
        var lines = PrintCommentSummaryPlanner.WrapOverlayText(
            "line one\nline two\nline three\nhidden-tail-token",
            maxWidth: 50,
            measureWidth: text => text.Length);

        lines.Should().Equal(
            "line one",
            "line two",
            "line three" + PrintCommentSummaryPlanner.Ellipsis);
    }

    [Fact]
    public void WrapOverlayText_TrimsLongUnbrokenTokenBeforeLaterWords()
    {
        var lines = PrintCommentSummaryPlanner.WrapOverlayText(
            new string('x', 12) + " hidden-tail-token",
            maxWidth: 6,
            measureWidth: text => text.Length);

        lines.Should().ContainSingle()
            .Which.Should().Be("xxxxx" + PrintCommentSummaryPlanner.Ellipsis);
    }
}
