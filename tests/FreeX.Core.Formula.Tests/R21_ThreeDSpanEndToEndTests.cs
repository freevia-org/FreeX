using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-21 follow-up: R21-crosssheet-3d-refs-2 and R21-information-functions-3 /
/// R21-crosssheet-3d-refs-3 were previously only fixed at the function-implementation level
/// (TryExpandSheetSpanAggregateRange's per-sheet materialization, SheetsFunc/SheetSpanCount)
/// without the FormulaEvaluator argument-expansion wiring that lets those implementations
/// actually run end-to-end:
///
///  - A full-column/full-row 3-D span (e.g. Sheet1:Sheet3!A:A) nominally spans the whole grid
///    on every sheet it covers. TryExpandSheetSpanAggregateRange called GetRangeValues with the
///    unclamped ~1,048,576-row extent per sheet, blowing the materialization cap and returning
///    #REF! even though bounded spans and whole-ROW spans already worked (see
///    R21_ThreeDSpanFullRowColumnRefsTests, which exercises the whole-row case and explicitly
///    notes the whole-column aggregate case was out of scope for that fix).
///
///  - SHEETS is a non-aggregate, non-structured function, so a literal span argument
///    (Sheet1:Sheet3!A1) hit EvaluateFunction's `isStructured || !isAggregate` branch and was
///    turned into ErrorValue.Value (#VALUE!) before SheetsFunc ever ran, even though SheetsFunc
///    itself (see R21_Sheets3DSpanCountTests, which drives it directly) already knows how to
///    count a span encoded in RangeValue.SheetName as "Start:End".
/// </summary>
public class R21_ThreeDSpanEndToEndTests
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

    [Fact]
    public void Sum_SheetSpan_FullColumn_SumsAcrossSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        // Before the fix: TryExpandSheetSpanAggregateRange called GetRangeValues with the
        // unclamped full-column extent (row 1..1,048,576) per sheet, exceeding the 1,000,000-cell
        // materialization cap and short-circuiting the whole SUM call to #REF!.
        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!A:A)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_SheetSpan_FullColumn_MultipleRowsPerSheet_SumsAcrossSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
        sheet3.SetCell(new CellAddress(sheet3.Id, 5, 1), new NumberValue(100));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!A:A)", sheet1, workbook);

        result.Should().Be(new NumberValue(1 + 10 + 2 + 3 + 100));
    }

    [Fact]
    public void Sheets_LiteralSpan_EndToEnd_ReturnsSheetCount()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        // Before the fix: EvaluateFunction's argument-expansion loop turned the span argument
        // into ErrorValue.Value before SheetsFunc ever ran, so this surfaced #VALUE! even though
        // SheetsFunc/SheetSpanCount already knew how to count a "Start:End" span.
        var result = _evaluator.Evaluate("=SHEETS(Sheet1:Sheet3!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sheets_SingleSheetReference_EndToEnd_ReturnsOne()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        var result = _evaluator.Evaluate("=SHEETS(Sheet1!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(1));
    }
}
