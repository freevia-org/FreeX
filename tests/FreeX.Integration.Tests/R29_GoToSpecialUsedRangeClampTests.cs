using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R29-performance-scale-correctness-2: GoToSpecialService.Find's
/// Blanks/Constants/Formulas/Comments/VisibleCellsOnly branches iterated every nominal cell of
/// the search range with no used-range clamp, so an explicit whole-sheet selection (e.g. Ctrl+A
/// twice, then Home &gt; Find &amp; Select &gt; Go To Special) turned into an effectively unbounded
/// ~17-billion-iteration scan even on a nearly empty workbook. Real Excel always intersects Go To
/// Special's search with the sheet's actual used range regardless of how much of the nominal grid
/// is selected, so FreeX must do the same once the selection is too large to scan directly.
/// </summary>
public class R29_GoToSpecialUsedRangeClampTests
{
    private static GridRange WholeSheet(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

    [Fact]
    public void FindBlanks_WholeSheetSelectionOnSparseWorkbook_ClampsToUsedRangeAndReturnsQuickly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5)); // only B2 populated

        // Explicit whole-sheet selection (Start != End), so WorkbookSession's single-cell-collapse
        // fallback to GetUsedRange() never kicks in before reaching GoToSpecialService.
        var range = WholeSheet(sheet);

        var stopwatch = Stopwatch.StartNew();
        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);
        stopwatch.Stop();

        // Real Excel's Blanks search on a whole-sheet selection is bounded to the used range, so the
        // only candidate cell is B2 itself -- and it isn't blank.
        result.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "Go To Special must clamp an oversized selection to the used range instead of scanning " +
            "the ~17 billion nominal cells of the whole grid");
    }

    [Fact]
    public void FindConstants_WholeSheetSelectionOnSparseWorkbook_ClampsToUsedRangeAndFindsThePopulatedCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var populated = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(populated, new NumberValue(5));

        var range = WholeSheet(sheet);

        var stopwatch = Stopwatch.StartNew();
        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Constants);
        stopwatch.Stop();

        result.Should().Equal(populated);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FindBlanks_EmptyWorkbookWholeSheetSelection_ReturnsEmptyInsteadOfHanging()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var range = WholeSheet(sheet);

        var stopwatch = Stopwatch.StartNew();
        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);
        stopwatch.Stop();

        result.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FindBlanks_SmallExplicitSelectionBeyondUsedRange_StillFindsBlanksInTheWholeSelection()
    {
        // Sibling already-working case (also pinned by
        // GoToSpecialServiceTests.FindBlanks_ReturnsBlankAddressesInRange): an ordinary, modest,
        // explicit selection must keep scanning its full literal extent even when part of it falls
        // outside the sheet's used range -- only pathologically large selections get clamped.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 2));
    }
}
