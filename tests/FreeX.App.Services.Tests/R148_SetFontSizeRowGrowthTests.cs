using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for rowcol-sizing F3: Increase/Decrease Font Size (and the font-size box) used
/// to build one flat <c>SetRowHeightCommand</c> spanning the WHOLE row range of the selection, sized
/// purely from the target font size with no comparison to any row's current height. That
/// unconditionally overwrote every row's height in the span (shrinking a taller row, e.g. a
/// wrapped-text or merged-banner row, down to the flat computed height) and cleared every row's
/// hidden flag in the span (silently un-hiding a hidden row caught inside it) -- see
/// SheetLayoutCommands.cs's SetRowHeightCommand.Apply. WorkbookSession.CreateSetFontSizeCommand now
/// emits one per-row command only for rows that actually need to grow, and skips hidden rows
/// entirely (mirroring CreateWrapTextGrowthCommands' existing "only ever grows a row, skips hidden
/// rows" contract).
/// </summary>
public sealed class R148_SetFontSizeRowGrowthTests
{
    [Fact]
    public void SetSelectedRangeFontSize_TallerRowInSpan_IsNotShrunkToTheFlatComputedHeight()
    {
        // Reproduces the finding's exact probe: row 1 has a tall custom height (e.g. a
        // wrapped-text/merged-banner row) unrelated to why the user is changing the font size on
        // this multi-row selection.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.RowHeights[1] = 120;

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));

        const double fontSize = 14;
        var fittingHeight = FontSizePlanner.EstimateFittingRowHeight(fontSize);
        fittingHeight.Should().Be(24); // matches the finding's evidence exactly

        var result = session.SetSelectedRangeFontSize(fontSize);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(120, "a row already taller than the newly computed flat height must not be shrunk by a font-size change");
    }

    [Fact]
    public void SetSelectedRangeFontSize_HiddenRowInSpan_IsNotSilentlyUnhidden()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.RowHeights[3] = 45;
        sheet.HiddenRows.Add(3);

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));

        var result = session.SetSelectedRangeFontSize(14);

        result.Success.Should().BeTrue();
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue("a hidden row caught in the selection's row span must not be revealed by an everyday font-size change");
        sheet.RowHeights[3].Should().Be(45, "a hidden row must keep its own stored height, not receive the flat computed height");
    }

    [Fact]
    public void SetSelectedRangeFontSize_EndToEndProbe_MatchesFindingBeforeAfter()
    {
        // The finding's full before/after probe in one test: row 1 tall + row 3 hidden, both inside
        // the same three-row selection the font-size change is applied to.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.RowHeights[1] = 120;
        sheet.RowHeights[3] = 45;
        sheet.HiddenRows.Add(3);

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));

        sheet.RowHeights[1].Should().Be(120);
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();

        var result = session.SetSelectedRangeFontSize(14);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(120, "before/after: row 1 must not collapse from 120px to the flat computed height");
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue("before/after: hidden row 3 must not reappear");
    }

    [Fact]
    public void SetSelectedRangeFontSize_RowNeedingToGrow_StillGrowsToTheFittingHeight()
    {
        // No-regression sibling: the fix must not stop the ordinary case -- a row at (or below) the
        // sheet's default height still grows to fit a larger font, exactly like before. Row 2 here
        // (between the tall row 1 and the hidden row 3) has no custom height at all.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.RowHeights[1] = 120;
        sheet.RowHeights[3] = 45;
        sheet.HiddenRows.Add(3);

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));

        const double fontSize = 14;
        var fittingHeight = FontSizePlanner.EstimateFittingRowHeight(fontSize);

        var result = session.SetSelectedRangeFontSize(fontSize);

        result.Success.Should().BeTrue();
        sheet.RowHeights[2].Should().Be(fittingHeight, "a row with no taller custom height must still grow to fit the new font size, matching the pre-fix behavior for the ordinary case");
    }

    [Fact]
    public void SetSelectedRangeFontSize_DecreasingFontOnASingleDefaultRow_StillAppliesTheStyleDiff()
    {
        // No-regression sibling covering the plain single-row/no-custom-height path already pinned
        // by WorkbookSessionFontSizeRowHeightClampTests: font size must still be applied and the row
        // still grows/sets normally when nothing taller or hidden is in play.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);
        session.SelectCell(new CellAddress(sheet.Id, 1, 1));

        var result = session.SetSelectedRangeFontSize(24);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(37); // ceil(24*96/72+5) = 37, same as the existing clamp test
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
