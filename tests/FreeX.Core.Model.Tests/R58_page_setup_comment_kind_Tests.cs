using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 58 fix verification:
/// - R58-io-page-setup-6-1: <see cref="WorksheetPageMargins.Normal"/> and
///   <see cref="WorksheetPageMargins.Narrow"/> must match real Excel's built-in margin-gallery
///   presets (Normal = 0.7"/0.7"/0.75"/0.75", Narrow = 0.25"/0.25"/0.75"/0.75"), and a brand new
///   <see cref="Sheet"/> must default to Excel's true blank-workbook margins (Normal), not Narrow.
/// - R58-render-comment-indicator-6-4: <see cref="WorksheetDisplayedComment"/> must carry the
///   <see cref="CellCommentDisplayKind"/> that produced the overlay so a print/PDF renderer can
///   recover the on-screen Note-vs-ThreadedComment-vs-Mixed indicator color.
/// </summary>
public sealed class R58_page_setup_comment_kind_Tests
{
    [Fact]
    public void WorksheetPageMargins_Normal_MatchesRealExcelPreset()
    {
        WorksheetPageMargins.Normal.Should().Be(new WorksheetPageMargins(0.7, 0.7, 0.75, 0.75));
    }

    [Fact]
    public void WorksheetPageMargins_Narrow_MatchesRealExcelPreset()
    {
        WorksheetPageMargins.Narrow.Should().Be(new WorksheetPageMargins(0.25, 0.25, 0.75, 0.75));
    }

    [Fact]
    public void WorksheetPageMargins_Wide_IsUnchangedAndStillCorrect()
    {
        // Sibling no-regression check: Wide was already correct and must not be touched.
        WorksheetPageMargins.Wide.Should().Be(new WorksheetPageMargins(1.25, 1.25, 1.0, 1.0));
    }

    [Fact]
    public void Sheet_NewSheet_DefaultsPageMarginsToNormal_NotNarrow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.PageMargins.Should().Be(WorksheetPageMargins.Normal);
    }

    [Fact]
    public void GetDisplayedCommentOverlays_ThreadedCommentOnly_HasThreadedCommentKind()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [a1] = new("check header", "Quinn")
        };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments: new Dictionary<CellAddress, string>(),
            threadedComments,
            pageRows: [1],
            pageColumns: [1]);

        overlays.Should().ContainSingle()
            .Which.Kind.Should().Be(CellCommentDisplayKind.ThreadedComment);
    }

    [Fact]
    public void GetDisplayedCommentOverlays_NoteAndThreadedCommentSameAddress_HasMixedKind()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var comments = new Dictionary<CellAddress, string> { [a1] = "legacy note" };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [a1] = new("check header", "Quinn")
        };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            threadedComments,
            pageRows: [1],
            pageColumns: [1]);

        overlays.Should().ContainSingle()
            .Which.Kind.Should().Be(CellCommentDisplayKind.Mixed);
    }

    [Fact]
    public void GetDisplayedCommentOverlays_LegacyNoteOnly_HasNoteKind()
    {
        // Sibling no-regression check: a plain legacy note (no threaded comment) keeps the
        // Note kind, matching the pre-existing on-screen red indicator.
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var comments = new Dictionary<CellAddress, string> { [a1] = "check header" };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1],
            pageColumns: [1]);

        overlays.Should().ContainSingle()
            .Which.Kind.Should().Be(CellCommentDisplayKind.Note);
    }
}
