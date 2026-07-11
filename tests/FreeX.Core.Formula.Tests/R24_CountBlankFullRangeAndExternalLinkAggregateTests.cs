using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-24 fix-bucket "fastaggregate-countblank-regression" regression tests.
///
/// R24-meta-1: Round 23 fixed COUNTBLANK(A:B) wrongly returning #REF! by removing the
/// used-range clamp AND the cell-count safety cap for CountBlank entirely, so a full-sheet
/// reference like COUNTBLANK(A:XFD)/COUNTBLANK(1:1048576) turned into a plain nested loop over
/// every one of the sheet's 1,048,576 x 16,384 nominal cells — a multi-billion iteration hang.
/// The fix keeps the un-clamped nominal cell count (for the correct total) but only scans the
/// used-range-clamped extent for non-blank cells, so the result is
/// (nominal cell count) - (non-blank cells found in the used range), computed near-instantly.
///
/// R24-external-links-1: SUM/AVERAGE/MIN/MAX/COUNT/COUNTBLANK/STDEV/VAR over a quoted
/// external-workbook range always evaluated to #REF!, because the fast-aggregate path's sheet
/// resolution (SheetEvalContext.ResolveSheetForFastRange) has no external-reference fallback,
/// even though the range was already accepted as valid via SheetExists (which does recognize
/// external references). The fix falls back to the same external-aware per-cell value lookup
/// (IEvalContext.GetCellValue) that the non-fast path already used, instead of returning #REF!
/// whenever the direct sheet lookup comes back null for a range that passed SheetExists.
/// </summary>
public sealed class R24_CountBlankFullRangeAndExternalLinkAggregateTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) MakeWb(params (uint row, uint col, ScalarValue val)[] cells)
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, r, c), v);
        return (wb, sheet);
    }

    [Fact]
    public void CountBlank_FullColumnSpanningEntireSheet_ComputesInsteadOfHanging()
    {
        // A:XFD is every column in the sheet (16,384 of them) combined with the full nominal row
        // extent (1,048,576) -- 17,179,869,184 nominal cells. Before the fix this was scanned
        // cell-by-cell with no cap at all (TryAcceptFastAggregateRange returned true
        // unconditionally for CountBlank and the used-range clamp explicitly excluded it), which
        // would hang for minutes. The fix must return near-instantly.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (3, 7, new TextValue("x")));

        var stopwatch = Stopwatch.StartNew();
        var result = _eval.Evaluate("=COUNTBLANK(A:XFD)", sheet, wb);
        stopwatch.Stop();

        var expectedTotalCells = (long)CellAddress.MaxRow * CellAddress.MaxCol;
        result.Should().Be(new NumberValue(expectedTotalCells - 2));
        stopwatch.Elapsed.Should().BeLessThan(
            MaxElapsedForHangGuard(),
            "COUNTBLANK over a full-sheet range must only scan the used-range-clamped extent, " +
            "never iterate the ~17 billion nominal cells");
    }

    [Fact]
    public void CountBlank_FullRowSpanningEntireSheet_ComputesInsteadOfHanging()
    {
        // 1:1048576 is every row in the sheet, combined with the full nominal column extent --
        // the full-row-reference sibling of the full-column case above, exercising the
        // FullRowRangeRefNode branch of TryResolveFastAggregateRange instead of
        // FullColumnRangeRefNode.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (3, 7, new TextValue("x")));

        var stopwatch = Stopwatch.StartNew();
        var result = _eval.Evaluate("=COUNTBLANK(1:1048576)", sheet, wb);
        stopwatch.Stop();

        var expectedTotalCells = (long)CellAddress.MaxRow * CellAddress.MaxCol;
        result.Should().Be(new NumberValue(expectedTotalCells - 2));
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForHangGuard());
    }

    [Fact]
    public void CountBlank_EmptySheetFullColumnRange_ReturnsWholeNominalCountInstantly()
    {
        // No populated cells at all: the used-range clamp finds no overlap, so every nominal cell
        // in the full-column range is blank.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        var result = _eval.Evaluate("=COUNTBLANK(A:XFD)", sheet, wb);

        var expectedTotalCells = (long)CellAddress.MaxRow * CellAddress.MaxCol;
        result.Should().Be(new NumberValue(expectedTotalCells));
    }

    [Fact]
    public void CountBlank_BoundedPartialRange_StillCountsExactly()
    {
        // An ordinary bounded (non-full-column/row) range must be completely unaffected by the
        // nominal-cell-count bookkeeping added for the full-range fix: NominalCellCount stays
        // null and the scanned rectangle already covers every cell that needs counting.
        var (wb, sheet) = MakeWb(
            (2, 2, new NumberValue(1)),
            (4, 3, new TextValue("x")));

        var result = _eval.Evaluate("=COUNTBLANK(B2:D6)", sheet, wb);

        // B2:D6 is 3 columns x 5 rows = 15 cells; 2 are non-blank (B2, C4).
        result.Should().Be(new NumberValue(13));
    }

    [Fact]
    public void Sum_ExternalWorkbookQuotedSheetRange_UsesCachedValueInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb();

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(100);
        cachedSheet.Values[(2u, 1u)] = new NumberValue(400);
        link.CachedSheetData.Add(cachedSheet);
        wb.ExternalLinks.Add(link);

        // SUM is one of the "fast aggregate" kinds; before the fix this always evaluated to
        // #REF! for a quoted external range because ResolveFastAggregateSheet (via
        // SheetEvalContext.ResolveSheetForFastRange) has no external-link fallback, even though
        // SheetExists (used to validate the range) does recognize the external reference.
        var result = _eval.Evaluate("=SUM('[Data File.xlsx]Sheet1'!A1:A2)", sheet, wb);

        result.Should().Be(new NumberValue(500));
    }

    [Fact]
    public void CountBlank_ExternalWorkbookQuotedSheetRange_UsesCachedValuesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb();

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(10);
        cachedSheet.Values[(2u, 1u)] = new TextValue("");
        cachedSheet.Values[(3u, 1u)] = new NumberValue(20);
        link.CachedSheetData.Add(cachedSheet);
        wb.ExternalLinks.Add(link);

        var result = _eval.Evaluate("=COUNTBLANK('[Data File.xlsx]Sheet1'!A1:A3)", sheet, wb);

        result.Should().Be(new NumberValue(1));
    }

    private static TimeSpan MaxElapsedForHangGuard()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(5);
    }
}
