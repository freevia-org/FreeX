using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-10 code-review regression coverage (group COMMENTS-IO, finding P1 -- 3rd meta-catch of
/// the threaded-comment legacy-mirror detection).
///
/// Real Excel 365 writes the legacy comments1.xml/VML "note" mirror of a threaded comment's root
/// text using a FIXED compatibility banner ("[Threaded comment]\n\n...\n\nComment:\n    {text}"),
/// never the "{Author}:\n{RootText}" form the detector previously assumed. Without recognizing
/// the real banner, every Excel-authored threaded comment would surface a bogus "Mixed"
/// (comment-and-note) display instead of the plain threaded-comment display Excel itself shows.
/// </summary>
public class FreeXReview10ViewportCommentMirrorTests
{
    [Fact]
    public void GetViewport_ThreadedCommentWithRealExcelBannerMirror_DisplaysAsThreadedCommentOnly()
    {
        // Arrange: model the exact shape real Excel 365 produces -- a threaded comment plus a
        // companion legacy "note" whose text is the fixed compatibility banner (not
        // "{Author}:\n{RootText}").
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);

        sheet.ThreadedComments[address] = new ThreadedComment("Please review the total", "Quinn");
        sheet.Comments[address] =
            "[Threaded comment]\n\nYour version of Excel allows you to read this threaded " +
            "comment; however, any edits made to it will get removed if the file is opened in a " +
            "newer version of Excel.\n\nComment:\n    Please review the total";

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        // Assert: the display must show only the threaded comment -- exactly what Excel itself
        // shows -- not a bogus "Mixed" comment-and-note with the Microsoft banner as a "Note".
        var dc = vp.Cells.Single(c => c.Row == 2 && c.Col == 2);
        dc.CommentDisplay.Should().NotBeNull();
        dc.CommentDisplay!.Kind.Should().Be(CellCommentDisplayKind.ThreadedComment,
            "Excel's legacy banner mirror must be suppressed, not shown as a separate Note (regression: real Excel never writes '{Author}:\\n{RootText}')");
        dc.CommentDisplay.Title.Should().Be("Comment");
        dc.CommentDisplay.Body.Should().NotContain("[Threaded comment]",
            "the Microsoft compatibility banner must never leak into the user-facing display");
    }

    [Fact]
    public void GetViewport_GenuineNoteThatHappensToCoexistWithThreadedComment_StillShowsMixed()
    {
        // A real, independently-authored note (not Excel's banner mirror) coexisting with a
        // threaded comment must still combine into Mixed -- this guards against over-broadly
        // suppressing every note next to a threaded comment.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 3);

        sheet.ThreadedComments[address] = new ThreadedComment("Thread body", "FreeX");
        sheet.Comments[address] = "A genuine separate note left by someone else";

        var vp = new ViewportService().GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 3 && c.Col == 3);
        dc.CommentDisplay!.Kind.Should().Be(CellCommentDisplayKind.Mixed,
            "a genuine independently-authored note must still be shown, not silently dropped");
    }
}
