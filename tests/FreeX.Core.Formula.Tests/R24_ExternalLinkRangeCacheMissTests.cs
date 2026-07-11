using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-24 fix-bucket "formulaeval-contexts-extlink" regression test.
///
/// R24-external-links-2: <see cref="FormulaEvaluator"/>'s <c>SheetEvalContext.GetCellValue</c>
/// (the scalar case) already treats a cache miss on a resolvable external-workbook reference as
/// "cannot tell genuinely-blank apart from never-refreshed" and throws <see cref="FormulaParseException"/>
/// so the caller (RecalcEngine) preserves the cell's last-known loaded value instead of overwriting it
/// with a recomputed 0/blank. The range-shaped sibling, <c>SheetEvalContext.GetRangeValues(sheetName,
/// ...)</c>, used to discard <c>ExternalLinkModel.TryGetCachedValue</c>'s bool return and always
/// substitute <see cref="BlankValue"/> for an uncached cell — silently corrupting the result of any
/// range function that isn't on the small "fast aggregate" list (SUM/AVERAGE/MIN/MAX/COUNT/STDEV/VAR),
/// e.g. MEDIAN, PRODUCT, VLOOKUP, LARGE. This pins the fix that mirrors the scalar behavior.
/// </summary>
public sealed class R24_ExternalLinkRangeCacheMissTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Workbook workbook, Sheet sheet) MakeWorkbookWithPartiallyCachedExternalLink()
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

        // A real sheetDataSet entry exists for the sheet, but it only cached A1 (=10) — A2 was never
        // referenced by any formula at last refresh, so it has no <cell> entry at all.
        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(10);
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        return (workbook, sheet);
    }

    [Fact]
    public void RangeFunction_OutsideFastAggregateList_TreatsUncachedExternalCell_LikeScalarCase()
    {
        var (workbook, sheet) = MakeWorkbookWithPartiallyCachedExternalLink();

        // MEDIAN is not one of the "fast aggregate" kinds (Sum/Average/Min/Max/Count/Stdev/Var), so it
        // evaluates through the generic GetRangeValues(sheetName, ...) path that had the bug.
        var rangeResult = _evaluator.Evaluate(
            "=MEDIAN('[Data File.xlsx]Sheet1'!A1:A2)", sheet, workbook);

        // The scalar reference to the very same uncached cell (A2) already throws
        // FormulaParseException, which FormulaEvaluator.Evaluate converts to #VALUE! for a direct
        // caller (exactly what RecalcEngine's guard would instead treat as "preserve the loaded
        // value"). The range case must behave identically instead of silently computing MEDIAN(10,
        // blank) = 10 as if A2 were a genuine blank.
        var scalarResult = _evaluator.Evaluate(
            "='[Data File.xlsx]Sheet1'!A2", sheet, workbook);

        scalarResult.Should().Be(ErrorValue.Value,
            "the scalar sibling already treats an uncached external cell as unresolvable, not blank");
        rangeResult.Should().Be(ErrorValue.Value,
            "a range-shaped external reference with an uncached cell must be treated exactly like " +
            "the scalar case (unresolvable), not silently substitute Blank and compute over it");
    }

    [Fact]
    public void RangeFunction_OutsideFastAggregateList_WithFullyCachedExternalRange_StillComputesCorrectly()
    {
        // Guard against over-broadening the fix: when every cell in the range IS cached, the range
        // function must still use the cached values (not fall into the throw/preserve path).
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

        var result = _evaluator.Evaluate("=MEDIAN('[Data File.xlsx]Sheet1'!A1:A2)", sheet, workbook);

        result.Should().Be(new NumberValue(15));
    }
}
