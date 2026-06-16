using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void AnchorArray_SpillAnchor_ReturnsFullSpillRangeValue()
    {
        // Arrange: sheet where A1 is the spill anchor for a 3×1 range {1,2,3}.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(1)));
        var rv = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        }, 1, 1);
        sheet.SetSpillRange(anchorAddr, rv);

        // Act: ANCHORARRAY(A1) should return the whole spill RangeValue.
        var result = _eval.Evaluate("=ANCHORARRAY(A1)", sheet);

        // Assert
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(2));
        range.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void AnchorArray_SpillAnchor_SumOfSpillRangeIsCorrect()
    {
        // Arrange: same as above.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(1)));
        var rv = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        }, 1, 1);
        sheet.SetSpillRange(anchorAddr, rv);

        // Act: SUM(ANCHORARRAY(A1)) = 1+2+3 = 6
        var result = _eval.Evaluate("=SUM(ANCHORARRAY(A1))", sheet);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void AnchorArray_NotASpillAnchor_ReturnsRefError()
    {
        // A cell that is not a spill anchor should give #REF!
        var sheet = MakeSheet((1, 1, new NumberValue(42)));

        var result = _eval.Evaluate("=ANCHORARRAY(A1)", sheet);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Sort_OverAnchorArraySpillRange_SortsTheSpilledValues()
    {
        // Models Spill Formulae!C190 = SORT(ANCHORARRAY(C184)) where C184 spills a 3x3 block.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 184, 3);
        sheet.SetCell(anchor, Cell.FromValue(new NumberValue(12)));
        var rv = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(12), new NumberValue(12.1), new NumberValue(12.2) },
            { new NumberValue(11), new NumberValue(11.1), new NumberValue(11.2) },
            { new NumberValue(13), new NumberValue(13.1), new NumberValue(13.2) },
        }, 184, 3);
        sheet.SetSpillRange(anchor, rv);

        var result = _eval.Evaluate("=SORT(ANCHORARRAY(C184))", sheet)
            .Should().BeOfType<RangeValue>("SORT over a spilled range must sort, not error").Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(3);
        result.Cells[0, 0].Should().Be(new NumberValue(11), "ascending sort by column 1 puts 11 first");
        result.Cells[1, 0].Should().Be(new NumberValue(12));
        result.Cells[2, 0].Should().Be(new NumberValue(13));
    }

    [Fact]
    public void AnchorArray_SpillAnchorSubtraction_ReturnsCorrectDifference()
    {
        // Models ANCHORARRAY(Z6)-ANCHORARRAY(X6) from the real workbook.
        // Two anchors with 3-row spills; their difference should be element-wise.
        var sheet = new Sheet(SheetId.New(), "S");

        // X6 (row 6, col 24): spill {10, 20, 30}
        var anchorX = new CellAddress(sheet.Id, 6, 24);
        sheet.SetCell(anchorX, Cell.FromValue(new NumberValue(10)));
        var rvX = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(10) },
            { new NumberValue(20) },
            { new NumberValue(30) }
        }, 6, 24);
        sheet.SetSpillRange(anchorX, rvX);

        // Z6 (row 6, col 26): spill {15, 25, 35}
        var anchorZ = new CellAddress(sheet.Id, 6, 26);
        sheet.SetCell(anchorZ, Cell.FromValue(new NumberValue(15)));
        var rvZ = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(15) },
            { new NumberValue(25) },
            { new NumberValue(35) }
        }, 6, 26);
        sheet.SetSpillRange(anchorZ, rvZ);

        // Evaluate: ANCHORARRAY(Z6)-ANCHORARRAY(X6) at cell AA6 (row 6, col 27)
        var result = _eval.Evaluate("=ANCHORARRAY(Z6)-ANCHORARRAY(X6)", sheet,
            workbook: null, new CellAddress(sheet.Id, 6, 27));

        // The result at (0,0) should be 15-10=5
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.Cells[0, 0].Should().Be(new NumberValue(5));
        range.Cells[1, 0].Should().Be(new NumberValue(5));
        range.Cells[2, 0].Should().Be(new NumberValue(5));
    }
}
