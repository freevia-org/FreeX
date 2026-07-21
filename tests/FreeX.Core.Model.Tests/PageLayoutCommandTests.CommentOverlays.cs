using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void WorksheetPageLayout_GetDisplayedCommentOverlays_ReturnsOnlyCommentsOnPrintedPage()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var c2 = new CellAddress(sheetId, 2, 3);
        var outside = new CellAddress(sheetId, 9, 9);
        var comments = new Dictionary<CellAddress, string>
        {
            [outside] = "outside",
            [c2] = "review total",
            [a1] = "check header"
        };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1, 2],
            pageColumns: [1, 3]);

        overlays.Should().Equal(
            new WorksheetDisplayedComment(a1, "check header", 0, 0),
            new WorksheetDisplayedComment(c2, "review total", 1, 1));
    }

    [Fact]
    public void WorksheetPageLayout_GetDisplayedCommentOverlays_IncludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var b2 = new CellAddress(sheetId, 2, 2);
        var c2 = new CellAddress(sheetId, 2, 3);
        var outside = new CellAddress(sheetId, 9, 9);
        var comments = new Dictionary<CellAddress, string>
        {
            [c2] = "legacy note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [outside] = new("outside"),
            [a1] = new("check header", "Quinn"),
            [b2] = new("review total", "Codex")
        };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            threadedComments,
            [1, 2],
            [1, 2, 3]);

        overlays.Should().Equal(
            new WorksheetDisplayedComment(a1, "check header", 0, 0, CellCommentDisplayKind.ThreadedComment),
            new WorksheetDisplayedComment(b2, "review total", 1, 1, CellCommentDisplayKind.ThreadedComment),
            new WorksheetDisplayedComment(c2, "legacy note", 1, 2, CellCommentDisplayKind.Note));
    }
}
