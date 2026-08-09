using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R76-formula-stack-array-4-1: CHOOSEROWS/CHOOSECOLS had no output-size cap, so e.g.
/// =CHOOSEROWS(A1:XFD1, SEQUENCE(1000000,1,1,0)) selects row 1 one million times across a
/// 16,384-column array and allocates a 1,000,000 x 16,384 ScalarValue[,] -- an OOM/crash risk.
/// Every sibling reshape function (VSTACK/HSTACK/TOROW/TOCOL/WRAPROWS/WRAPCOLS/EXPAND in
/// BuiltInFunctions.DynamicArrays.Stacking.cs) already checked "product > 1_000_000 -> #VALUE!"
/// before allocating; CHOOSEROWS/CHOOSECOLS mirrored that cap.
///
/// R127 update: that shared cell-count cap was raised from the stale hardcoded 1,000,000 to
/// FormulaSafetyLimits.MaxMaterializedRangeCells (16,777,216) across every one of those sibling
/// functions plus CHOOSEROWS/CHOOSECOLS, matching r126's fix to the same constant's other call
/// sites (BuildRangeValue/OFFSET/INDIRECT/aggregates). The "huge" cases here now use sizes well
/// beyond the real 16,777,216 cap so they still correctly return #VALUE!; see
/// Choosecols_1_2MillionCells_NowUnderRaisedCap_StillSpillsSelection for a case that used to be
/// wrongly rejected under the old cap and now legitimately succeeds.
///
/// The source arrays below are built with SEQUENCE(...) rather than a literal worksheet range: a
/// literal full-row/full-column reference (e.g. A1:XFD1 or A:A) is clamped to the sheet's used
/// range before materializing (FormulaEvaluator.References.cs's ClampOpenEndedRangeToUsed), which
/// would silently shrink the array and defeat the point of this test. SEQUENCE produces a plain
/// computed array of the exact requested size with no such clamping.
/// </summary>
public sealed class R76_ChooseRowsColsSizeCapTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Chooserows_HugeRepeatedIndexAcrossWideRange_ReturnsValueErrorNotOom()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        // SEQUENCE(1,16384) is a 1 x 16,384 array (Excel's full row width). SEQUENCE(1000000,1,1,0)
        // yields the value 1 repeated one million times (start=1, step=0), so ChooseRows would
        // otherwise try to allocate a 1,000,000 x 16,384 result.
        var result = _eval.Evaluate("=CHOOSEROWS(SEQUENCE(1,16384),SEQUENCE(1000000,1,1,0))", sheet);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Choosecols_HugeRepeatedIndexAcrossTallRange_ReturnsValueErrorNotOom()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        // SEQUENCE(20000,1) is a 20,000-row x 1-col array. Selecting its single column 1,000
        // times (SEQUENCE(1,1000,1,0) is 1 repeated 1,000 times) would otherwise allocate a
        // 20,000 x 1,000 = 20,000,000-cell result -- over the real shared
        // FormulaSafetyLimits.MaxMaterializedRangeCells cap (16,777,216; R127).
        var result = _eval.Evaluate("=CHOOSECOLS(SEQUENCE(20000,1),SEQUENCE(1,1000,1,0))", sheet);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Choosecols_1_2MillionCells_NowUnderRaisedCap_StillSpillsSelection()
    {
        // R127: CHOOSECOLS used to enforce its own hardcoded 1,000,000-cell cap independent of
        // FormulaSafetyLimits.MaxMaterializedRangeCells (now 16,777,216 -- see r126). A
        // 2,000-row x 1-col array with its single column repeated 600 times is 1,200,000 cells,
        // over the OLD stale cap but comfortably under the real shared one, so it must now
        // succeed instead of wrongly returning #VALUE!.
        var sheet = new Sheet(SheetId.New(), "S");

        var result = _eval.Evaluate("=CHOOSECOLS(SEQUENCE(2000,1),SEQUENCE(1,600,1,0))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2000);
        rv.ColCount.Should().Be(600);
    }

    [Fact]
    public void Chooserows_NormalSmallSelection_StillSpillsSelectedRows()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:A5,{2;4})", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Choosecols_WithinCapLargeChoice_StillWorks()
    {
        // A1:A1000 is 1,000 rows x 1 col; repeating column 1 as the choice 1,000 times (via
        // SEQUENCE(1,1000,1,0), which is 1 repeated 1,000 times) produces a 1,000 x 1,000 = exactly
        // 1,000,000-cell result -- right at the cap boundary, so it must still succeed (the guard
        // is a strict "> 1_000_000", matching the sibling reshape functions).
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint r = 1; r <= 1000; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:A1000,SEQUENCE(1,1000,1,0))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1000);
        rv.ColCount.Should().Be(1000);
    }
}
