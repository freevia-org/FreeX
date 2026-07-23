using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-80 fix-bucket "calc-external-ref-5" regression test.
///
/// R80-calc-external-ref-5-1: Excel's INDIRECT requires the referenced external workbook to be
/// actually open in the same session -- unlike a direct cell/range formula reference (e.g.
/// ='[Data File.xlsx]Sheet1'!A1, pinned as correct by R24_ExternalLinkRangeCacheMissTests), it
/// never falls back to an externalLink's cached values and always returns #REF! when the source
/// workbook is closed/unavailable. FreeX previously special-cased nothing for an
/// INDIRECT-originated external-sheet lookup, so both the scalar
/// (IndirectCore -> ctx.GetCellValue(sheetName, ...)) and range-shaped
/// (TryResolveIndirectRangeReference -> CompleteIndirectRange, gated only by ctx.SheetExists)
/// paths silently resolved the externalLink's cached value instead -- exactly like a direct
/// reference, diverging from Excel's documented INDIRECT-external-requires-open behavior.
/// </summary>
public sealed class R80_IndirectExternalClosedWorkbookTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Workbook workbook, Sheet sheet) MakeWorkbookWithCachedExternalLink()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(10);
        cachedSheet.Values[(2u, 1u)] = new NumberValue(20);
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        return (workbook, sheet);
    }

    [Fact]
    public void Indirect_ScalarQuotedExternalReference_ToClosedWorkbook_ReturnsRef()
    {
        var (workbook, sheet) = MakeWorkbookWithCachedExternalLink();

        var result = _evaluator.Evaluate(
            "=INDIRECT(\"'[Data File.xlsx]Sheet1'!A1\")", sheet, workbook);

        result.Should().Be(ErrorValue.Ref,
            "INDIRECT to a closed external workbook must return #REF!, never the externalLink's " +
            "cached value -- Excel's INDIRECT requires the source workbook to be actually open");
    }

    [Fact]
    public void Indirect_RangeQuotedExternalReference_ToClosedWorkbook_ReturnsRef()
    {
        var (workbook, sheet) = MakeWorkbookWithCachedExternalLink();

        // SUM(INDIRECT(...)) exercises the fast-aggregate range path (FormulaEvaluator.
        // FastAggregates.cs), which consumes TryResolveIndirectRangeReference/CompleteIndirectRange
        // directly rather than going through BuildIndirectRange.
        var result = _evaluator.Evaluate(
            "=SUM(INDIRECT(\"'[Data File.xlsx]Sheet1'!A1:A2\"))", sheet, workbook);

        result.Should().Be(ErrorValue.Ref,
            "a range-shaped INDIRECT to a closed external workbook must also return #REF!, not " +
            "silently sum the externalLink's cached values");
    }

    [Fact]
    public void DirectReference_ToClosedExternalWorkbook_StillReturnsCachedValue()
    {
        // No-regression sibling: a direct (non-INDIRECT) formula reference to the same closed
        // external workbook must be unaffected by the INDIRECT-specific fix and keep resolving the
        // externalLink's cached value, matching Excel and R24_ExternalLinkRangeCacheMissTests.
        var (workbook, sheet) = MakeWorkbookWithCachedExternalLink();

        var result = _evaluator.Evaluate("='[Data File.xlsx]Sheet1'!A1", sheet, workbook);

        result.Should().Be(new NumberValue(10),
            "a direct external-workbook cell reference still legitimately falls back to the " +
            "cached value -- only INDIRECT's own resolution must be tightened");
    }
}
