using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public class CrossSheetReferenceTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void CrossSheetCellRef_ReadsValueFromOtherSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("=Sheet2!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void QuotedCrossSheetCellRef_ReadsValueFromSheetWithSpace()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("My Sheet");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("='My Sheet'!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void QuotedCrossSheetCellRef_ReadsValueFromSheetWithApostrophe()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Bob's Sheet");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(99));

        var result = _evaluator.Evaluate("='Bob''s Sheet'!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void CrossSheetRange_SumWorksAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(Sheet2!A1:A3)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void CrossSheetCellRef_RowReturnsReferencedRow()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 5, 2), new NumberValue(42));

        var result = _evaluator.Evaluate("=ROW(Sheet2!B5)", sheet1, workbook);

        result.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void CrossSheetCellRef_ColumnReturnsReferencedColumn()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 5, 2), new NumberValue(42));

        var result = _evaluator.Evaluate("=COLUMN(Sheet2!B5)", sheet1, workbook);

        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void NamedSingleCellReference_RowReturnsReferencedRow()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("Target", new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, 5, 2)));

        var result = _evaluator.Evaluate("=ROW(Target)", sheet, workbook);

        result.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void NamedRangeReference_RowsReturnsRangeHeight()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("Block", new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3)));

        var result = _evaluator.Evaluate("=ROWS(Block)", sheet, workbook);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void NamedRangeReference_ArrayExpressionUsesFullRange()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));
        workbook.DefineNamedRange("MyData", new GridRange(a1, a3));

        var result = _evaluator.Evaluate("=SUM(MyData*2)", sheet, workbook);

        result.Should().Be(new NumberValue(12));
    }

    [Fact]
    public void NamedRangeReference_BareReferenceSpillsFullRange()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));
        workbook.DefineNamedRange("MyData", new GridRange(a1, a3));

        var result = _evaluator.Evaluate("=MyData", sheet, workbook)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(2));
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Theory]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    public void NamedRangeReference_OutOfGridA1ShapedName_ResolvesAsName(string name)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var target = new CellAddress(sheet.Id, 4, 2);
        sheet.SetCell(target, new NumberValue(42));
        workbook.DefineNamedRange(name, new GridRange(target, target));

        var result = _evaluator.Evaluate("=" + name, sheet, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(42));
    }

    [Theory]
    [InlineData("=XFE1")]
    [InlineData("=A1048577")]
    public void NamedRangeReference_UnboundOutOfGridA1ShapedName_ReturnsNameError(string formula)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate(formula, sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void CrossSheetRef_UnknownSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=NonExistent!A1", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void CrossSheetRange_UnknownSheetInAggregate_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=SUM(NonExistent!A1:A2)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void CrossSheetRef_UnknownSheetInRow_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=ROW(NonExistent!B5)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void CrossSheetRef_UnknownSheetInCountblank_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=COUNTBLANK(NonExistent!B5)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void SameSheetRef_StillWorks()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(99));

        var result = _evaluator.Evaluate("=A1", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    // ── Safety: inverted range references ─────────────────────────────────

    [Fact]
    public void InvertedRange_VLOOKUP_DoesNotCrash()
    {
        // B5:A1 is an inverted range — must not throw ArgumentOutOfRangeException
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));

        // VLOOKUP uses BuildRangeValue; inverted row/col should not crash
        var result = _evaluator.Evaluate("=VLOOKUP(10,B1:A1,2,FALSE)", sheet);

        // Result may be an error (lookup not found in inverted range) but must not throw
        result.Should().NotBeNull();
    }

    [Fact]
    public void InvertedRange_SUM_ReturnsZeroOrValue()
    {
        // SUM with inverted range (B3:A1) uses GetRangeValues which handles gracefully
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));

        var result = _evaluator.Evaluate("=SUM(A2:A1)", sheet);

        result.Should().NotBeNull();
    }

}
