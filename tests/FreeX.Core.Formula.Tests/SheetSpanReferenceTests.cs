using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// 3-D sheet-span references (e.g. Sheet1:Sheet3!A1, Sheet1:Sheet3!A1:B5) — Finding H28.
/// Excel expands these across every sheet from the start sheet to the end sheet inclusive
/// (in workbook tab order, including sheets strictly between them) when used as an argument
/// to an aggregate function (SUM, AVERAGE, COUNT, ...). Everywhere else a 3-D reference is
/// a #VALUE! error; a missing sheet name is #REF!.
/// </summary>
public class SheetSpanReferenceTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static Workbook ThreeSheetWorkbook(out Sheet sheet1, out Sheet sheet2, out Sheet sheet3)
    {
        var workbook = new Workbook("Test");
        sheet1 = workbook.AddSheet("Sheet1");
        sheet2 = workbook.AddSheet("Sheet2");
        sheet3 = workbook.AddSheet("Sheet3");
        return workbook;
    }

    // ── SUM over a 3-D span: single cell ──────────────────────────────────────

    [Fact]
    public void Sum_SheetSpanSingleCell_SumsAllThreeSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_SheetSpan_IncludesSheetsBetweenStartAndEnd()
    {
        // A 4-sheet workbook where the span only names the first and last sheet must still
        // include Sheet2 and Sheet3 (the sheets strictly between them in tab order).
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var sheet4 = workbook.AddSheet("Sheet4");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(20));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(30));
        sheet4.SetCell(new CellAddress(sheet4.Id, 1, 1), new NumberValue(40));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet4!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void Sum_ReversedSheetSpan_EqualsNormalSpan()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        var forward = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!A1)", sheet1, workbook);
        var reversed = _evaluator.Evaluate("=SUM(Sheet3:Sheet1!A1)", sheet1, workbook);

        forward.Should().Be(new NumberValue(6));
        reversed.Should().Be(forward);
    }

    // ── Range form: SUM/COUNT/MAX over Sheet1:Sheet2!A1:B2 ────────────────────

    [Fact]
    public void Sum_SheetSpanRange_SumsAllCellsAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // A1:B2 = 1,2,3,4 on each sheet -> per-sheet sum 10, two sheets -> 20
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new NumberValue(2));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(3));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(4));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(3));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new NumberValue(4));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet2!A1:B2)", sheet1, workbook);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Count_SheetSpanRange_CountsNumericCellsAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new TextValue("x"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=COUNT(Sheet1:Sheet2!A1:B2)", sheet1, workbook);

        // 4 cells per sheet x 2 sheets = 8 cells scanned; 3 are numeric (A1 on each sheet + A2 on Sheet2)
        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Max_SheetSpanRange_ReturnsMaxAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(5));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new NumberValue(99));

        var result = _evaluator.Evaluate("=MAX(Sheet1:Sheet2!A1:B2)", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Average_SheetSpanSingleCell_AveragesAcrossSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(20));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(30));

        var result = _evaluator.Evaluate("=AVERAGE(Sheet1:Sheet3!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(20));
    }

    // ── Errors ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sum_SheetSpan_MissingEndSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=SUM(Sheet1:NoSuchSheet!A1)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Sum_SheetSpan_MissingStartSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        var result = _evaluator.Evaluate("=SUM(NoSuchSheet:Sheet2!A1)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void BareSheetSpanReference_NonAggregateContext_ReturnsValueError()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));

        var result = _evaluator.Evaluate("=Sheet1:Sheet3!A1", sheet1, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SheetSpanReference_AsArithmeticOperand_ReturnsValueError()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));

        var result = _evaluator.Evaluate("=Sheet1:Sheet3!A1+1", sheet1, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SheetSpanReference_AsStructuredFunctionArgument_ReturnsValueError()
    {
        // INDEX is a structured (2-D access) function, not an aggregate -> a span argument is #VALUE!.
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));

        var result = _evaluator.Evaluate("=INDEX(Sheet1:Sheet3!A1:B5,1,1)", sheet1, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    // ── Quoted sheet names ──────────────────────────────────────────────────────

    [Fact]
    public void Sum_QuotedSheetSpan_SheetNamesWithSpaces_SumsAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var first = workbook.AddSheet("First Sheet");
        var middle = workbook.AddSheet("Middle Sheet");
        var last = workbook.AddSheet("Last Sheet");
        first.SetCell(new CellAddress(first.Id, 1, 1), new NumberValue(1));
        middle.SetCell(new CellAddress(middle.Id, 1, 1), new NumberValue(2));
        last.SetCell(new CellAddress(last.Id, 1, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM('First Sheet:Last Sheet'!A1)", first, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_MixedQuoting_UnquotedStartQuotedEnd_SumsAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var last = workbook.AddSheet("Last Sheet");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(4));
        last.SetCell(new CellAddress(last.Id, 1, 1), new NumberValue(5));

        var result = _evaluator.Evaluate("=SUM(Sheet1:'Last Sheet'!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(9));
    }

    // ── Parse/print round-trip ───────────────────────────────────────────────────

    [Theory]
    [InlineData("SUM(Sheet1:Sheet3!A1)")]
    [InlineData("SUM(Sheet1:Sheet3!A1:B5)")]
    [InlineData("SUM(Sheet3:Sheet1!A1)")]
    [InlineData("SUM('First Sheet:Last Sheet'!A1)")]
    public void SheetSpanReference_RoundTripsThroughParseAndSerialize(string formulaText)
    {
        var ast = FormulaEvaluator.ParseFormula(formulaText);
        var reprinted = FormulaSerializer.Serialize(ast);

        reprinted.Should().Be(formulaText);
    }
}
