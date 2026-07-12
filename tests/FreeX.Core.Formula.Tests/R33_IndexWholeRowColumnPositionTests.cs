using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R33-formula-lookup-legacy-2-1: INDEX(array,0,col) (whole-column form) and INDEX(array,row,0)
/// (whole-row form) must preserve the source range's real sheet position (StartRow/StartCol/
/// SheetName/IsSheetReference) so that ROW()/COLUMN() report the real sheet coordinates and
/// hidden-row-aware SUBTOTAL/AGGREGATE correctly exclude hidden rows over the result — mirroring
/// the anchored construction already used by OFFSET (FormulaEvaluator.References.cs) and by
/// INDEX's own scalar (fully-indexed) result. Before the fix, IndexScalar's rowNum==0/colNum==0
/// branches built `new RangeValue(...)` with no coordinates at all, so the result was
/// indistinguishable from a computed/virtual array (StartRow=1/StartCol=1/SheetName=null/
/// IsSheetReference=false) regardless of where the source table actually lived.
/// </summary>
public sealed class R33_IndexWholeRowColumnPositionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static Sheet MakeDataSheet()
    {
        // A5:D10 -- rows 5..10, columns A..D (1..4).
        var sheet = new Sheet(SheetId.New(), "S");
        int n = 1;
        for (int r = 5; r <= 10; r++)
            for (int c = 1; c <= 4; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(n++));
        return sheet;
    }

    [Fact]
    public void Row_OfIndexWholeColumn_ViaOffsetBase_ReturnsRealSheetRows()
    {
        // INDEX(OFFSET(A1,4,0,6,4),0,2) selects the whole 2nd column of a range anchored at A5:D10
        // (rows 5..10). ROW() over that result must report the real sheet rows {5;6;7;8;9;10}, not
        // {1;2;3;4;5;6} (which is what a position-less computed array would report).
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=ROW(INDEX(OFFSET(A1,4,0,6,4),0,2))", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(6);
        result.ColCount.Should().Be(1);
        for (int i = 0; i < 6; i++)
            result.At(i + 1, 1).Should().Be(new NumberValue(5 + i));
    }

    [Fact]
    public void Column_OfIndexWholeRow_ViaOffsetBase_ReturnsRealSheetColumns()
    {
        // INDEX(OFFSET(A1,4,0,6,4),3,0) selects the whole 3rd row (sheet row 7) of the same
        // A5:D10-anchored range. COLUMN() over that result must report {1,2,3,4} (columns A..D).
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=COLUMN(INDEX(OFFSET(A1,4,0,6,4),3,0))", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(4);
        for (int i = 0; i < 4; i++)
            result.At(1, i + 1).Should().Be(new NumberValue(1 + i));
    }

    [Fact]
    public void Subtotal_OverIndexWholeColumn_ViaFourArgAreaNumForm_ExcludesHiddenRow()
    {
        // INDEX(A5:D10,0,2,1) uses the explicit area_num=1 four-argument reference form (the other
        // path the review flagged as reaching the generic/slow IndexScalar). Column B of A5:D10 is
        // {1,2,3,4,5,6} across sheet rows 5..10; hide sheet row 6 (the 2nd data row) and confirm
        // SUBTOTAL(109,...) excludes it while SUBTOTAL(9,...) (no hidden-row exclusion) keeps it.
        var sheet = MakeDataSheet();
        sheet.GroupHiddenRows.Add(6);

        // Column B values across rows 5..10: 2,6,10,14,18,22 (with n starting at 1, col-major
        // layout A=1..4,B=... per row of 4). Row 5: 1,2,3,4 -> col B = 2. Row 6: 5,6,7,8 -> col B = 6.
        // Excluding hidden row 6 (value 6): 2+10+14+18+22 = 66.
        var withHiddenExcluded = _eval.Evaluate("=SUBTOTAL(109,INDEX(A5:D10,0,2,1))", sheet);
        var withHiddenIncluded = _eval.Evaluate("=SUBTOTAL(9,INDEX(A5:D10,0,2,1))", sheet);

        withHiddenExcluded.Should().Be(new NumberValue(66));
        withHiddenIncluded.Should().Be(new NumberValue(72));
    }

    [Fact]
    public void Row_OfIndexWholeColumn_OverComputedArrayBase_StaysPositionLess()
    {
        // Sibling already-working case: when the INDEX base is a genuinely computed array (not a
        // sheet reference), the whole-column result must remain position-less (StartRow=1),
        // exactly as before this fix -- proving the fix doesn't fabricate coordinates for bases
        // that never had any.
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=ROW(INDEX({1,2;3,4;5,6},0,2))", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(1));
        result.At(2, 1).Should().Be(new NumberValue(2));
        result.At(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Index_WholeColumnAndWholeRow_StillReturnCorrectValues()
    {
        // Already-working value-correctness sibling: the fix only adds coordinate/provenance
        // metadata, it must not change which values come back.
        var sheet = MakeDataSheet();

        var col = _eval.Evaluate("=INDEX(A5:D10,0,2)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;
        col.RowCount.Should().Be(6);
        col.ColCount.Should().Be(1);
        col.At(1, 1).Should().Be(new NumberValue(2));
        col.At(6, 1).Should().Be(new NumberValue(22));

        var row = _eval.Evaluate("=INDEX(A5:D10,3,0)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;
        row.RowCount.Should().Be(1);
        row.ColCount.Should().Be(4);
        row.At(1, 1).Should().Be(new NumberValue(9));
        row.At(1, 4).Should().Be(new NumberValue(12));
    }
}
