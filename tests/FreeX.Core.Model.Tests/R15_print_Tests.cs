using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 15 fix verification: <see cref="WorksheetPageLayout.GetDisplayedCommentOverlays"/> must
/// honor <see cref="Sheet.ShownComments"/> ("pinned" notes) when a caller passes the shown-comments
/// set, so print/PDF "As displayed on sheet" only draws boxes for notes the user actually pinned
/// open rather than every note/threaded comment on the sheet (R15-comments-threading-ui-2).
/// </summary>
public sealed class R15_print_Tests
{
    [Fact]
    public void GetDisplayedCommentOverlays_WithShownCommentsFilter_NoneShown_ReturnsNoOverlays()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var c2 = new CellAddress(sheetId, 2, 3);
        var comments = new Dictionary<CellAddress, string>
        {
            [a1] = "check header",
            [c2] = "review total"
        };
        var shownComments = new HashSet<CellAddress>(); // nothing pinned

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1, 2],
            pageColumns: [1, 3],
            shownComments: shownComments);

        overlays.Should().BeEmpty("no legacy note is pinned (Sheet.ShownComments is empty), " +
            "so 'As displayed on sheet' must not draw a box for any of them");
    }

    [Fact]
    public void GetDisplayedCommentOverlays_WithShownCommentsFilter_OnePinned_ReturnsOnlyThatOverlay()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var c2 = new CellAddress(sheetId, 2, 3);
        var comments = new Dictionary<CellAddress, string>
        {
            [a1] = "check header",
            [c2] = "review total"
        };
        var shownComments = new HashSet<CellAddress> { c2 }; // only c2 pinned open

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1, 2],
            pageColumns: [1, 3],
            shownComments: shownComments);

        overlays.Should().Equal(new WorksheetDisplayedComment(c2, "review total", 1, 1));
    }

    [Fact]
    public void GetDisplayedCommentOverlays_WithoutShownCommentsOverload_StillReturnsEveryOnPageComment()
    {
        // The pre-existing overload (no shownComments argument) must remain unfiltered for any
        // other caller that intentionally wants every on-page comment (e.g. the "At end of sheet"
        // print mode never calls the shown-comments overload).
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var comments = new Dictionary<CellAddress, string> { [a1] = "check header" };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1],
            pageColumns: [1]);

        overlays.Should().Equal(new WorksheetDisplayedComment(a1, "check header", 0, 0));
    }
}
