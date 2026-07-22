using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R74-formula-reference-fns-4-2: =ROW(A:A) directly spills the literal {1;...;1048576} array (the
/// TryEvaluateReferenceDimensionFunction fast path in FormulaEvaluator.References.cs bypasses
/// ClampOpenEndedRangeToUsed for ROW/COLUMN -- see R61_FormulaBucketATests). But =ROW(AllA) where
/// AllA is a name defined as Sheet1!$A:$A only ever spilled {1;...;used-range-end}, because a
/// NamedRangeNode argument never reached that fast path at all (TryAsRangeRef only recognizes
/// RangeRefNode/FullColumnRangeRefNode/FullRowRangeRefNode) and instead routed through the general
/// per-argument BuildRangeValue path, which DOES clamp. Fixed by resolving a NamedRangeNode
/// argument to its raw (unclamped) underlying range and feeding that into the same RAW ROW/COLUMN
/// computation the literal A:A form already uses.
/// </summary>
public sealed class R74_RowColumnNamedFullRangeTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeUsedRangeA1ToA10()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        return sheet;
    }

    [Fact]
    public void Row_OfNamedFullColumn_ReportsPositionsBeyondUsedRange_MatchingLiteralFullColumn()
    {
        // AllA = Sheet1!$A:$A, but the sheet's used range only reaches row 10 -- ROW(AllA) must
        // still be the literal {1;2;...;1048576} array (not clamped down to {1;...;10}), exactly
        // like the direct =ROW(A:A) form.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        workbook.DefineNamedRange(
            "AllA",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));

        var result = _eval.Evaluate("=ROW(AllA)", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be((int)CellAddress.MaxRow);
        result.ColCount.Should().Be(1);
        result.At(100, 1).Should().Be(new NumberValue(100));
        result.At((int)CellAddress.MaxRow, 1).Should().Be(new NumberValue(CellAddress.MaxRow));
    }

    [Fact]
    public void Column_OfNamedFullRow_ReportsFullColumnCount_MatchingLiteralFullRow()
    {
        // AllRow = Sheet1!$1:$1 -- COLUMN(AllRow) must spill the literal {1;2;...;16384} array,
        // exactly like the direct =COLUMN(1:1) form.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint c = 1; c <= 5; c++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, c), new NumberValue(c));
        workbook.DefineNamedRange(
            "AllRow",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, CellAddress.MaxCol)));

        var result = _eval.Evaluate("=COLUMN(AllRow)", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be((int)CellAddress.MaxCol);
        result.At(1, 50).Should().Be(new NumberValue(50));
        result.At(1, (int)CellAddress.MaxCol).Should().Be(new NumberValue(CellAddress.MaxCol));
    }

    [Fact]
    public void Row_OfNamedFiniteRange_Unchanged_SiblingNoRegression()
    {
        // A named finite (non-full-column/row) range must keep reporting its own bounded
        // positions, unaffected by the new fast-path branch.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange(
            "MyRange",
            new GridRange(
                new CellAddress(sheet.Id, 5, 2),
                new CellAddress(sheet.Id, 8, 2)));

        var result = _eval.Evaluate("=ROW(MyRange)", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(4);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(5));
        result.At(4, 1).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Sum_OfNamedFullColumn_StillClampsToUsedRange_SiblingNoRegression()
    {
        // Sibling no-regression: a non-ROW/COLUMN use of the same named full-column range (SUM)
        // must still clamp to the sheet's used range -- this fast path must only bypass the clamp
        // for ROW/COLUMN, never for aggregates.
        var workbook = new Workbook("Test");
        var s = workbook.AddSheet("Sheet1");
        s.SetCell(new CellAddress(s.Id, 1, 1), new NumberValue(10));
        s.SetCell(new CellAddress(s.Id, 2, 1), new NumberValue(20));
        s.SetCell(new CellAddress(s.Id, 3, 1), new NumberValue(30));
        workbook.DefineNamedRange(
            "AllA",
            new GridRange(
                new CellAddress(s.Id, 1, 1),
                new CellAddress(s.Id, CellAddress.MaxRow, 1)));

        var result = _eval.Evaluate("=SUM(AllA)", s, workbook);

        result.Should().Be(new NumberValue(60));
    }
}
