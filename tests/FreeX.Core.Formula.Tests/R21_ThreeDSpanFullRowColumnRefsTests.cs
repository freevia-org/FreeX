using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round 21 finding R21-crosssheet-3d-refs-2: 3-D sheet-span references (e.g.
/// Sheet1:Sheet3!A1) previously only accepted a plain cell/range reference part —
/// ParseSheetSpanBody unconditionally required a CellRef token and threw
/// FormulaParseException (surfaced as #VALUE! by FormulaEvaluator.Evaluate's catch-all)
/// for a whole-column (A:A) or whole-row (1:1) shape, even though the single-sheet path
/// (ParseSheetQualifiedReference) has always accepted both. Fixed by trying
/// TryParseFullColumnSpanBody/TryParseFullRowSpanBody first, producing a RangeRefNode
/// (Start row 1/col A .. End MaxRow/MaxCol) with EndSheetName set — mirroring exactly how
/// FormulaEvaluator.References.cs's ToRangeRef helpers convert the non-span
/// FullColumnRangeRefNode/FullRowRangeRefNode shapes, since those node types have no
/// EndSheetName slot of their own to carry a span.
/// </summary>
public class R21_ThreeDSpanFullRowColumnRefsTests
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

    // ── Whole-row span: parses AND evaluates correctly end-to-end ───────────────────────────
    // (A full-row span's nominal extent is 1 row x MaxCol (16,384) columns, well under the
    // evaluator's 1,000,000-cell materialization cap, so this is a complete pass/fail check
    // of the fix with no other machinery involved.)

    [Fact]
    public void Sum_SheetSpan_FullRow_SumsAcrossSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        // Before the fix: ParseSheetSpanBody threw FormulaParseException on the "1:1" shape,
        // caught by Evaluate() and surfaced as ErrorValue.Value (#VALUE!).
        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!1:1)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_SheetSpan_FullRow_AbsoluteRowMarkers_StillParsesAndSums()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(20));
        sheet3.SetCell(new CellAddress(sheet3.Id, 2, 1), new NumberValue(30));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!$2:$2)", sheet1, workbook);

        result.Should().Be(new NumberValue(60));
    }

    // ── Whole-column span: no longer fails to parse ─────────────────────────────────────────
    // (A full-column span's nominal extent is MaxRow (1,048,576) rows x 1 column, which exceeds
    // the evaluator's materialization cap for the direct-sum aggregate path — a separate,
    // pre-existing limitation of TryExpandSheetSpanAggregateRange in FormulaEvaluator.Functions.cs
    // that is out of scope for this parser-only fix. What this fix guarantees is that the span
    // now PARSES successfully instead of throwing/producing #VALUE!, which ISREF — a function
    // that only checks sheet existence, never materializes the range — demonstrates cleanly.)

    [Fact]
    public void IsRef_SheetSpan_FullColumn_RecognizedAsValidReference()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        // Before the fix: parsing "Sheet1:Sheet3!A:A" threw FormulaParseException, caught by
        // Evaluate() and surfaced as ErrorValue.Value (#VALUE!) rather than TRUE/FALSE.
        var result = _evaluator.Evaluate("=ISREF(Sheet1:Sheet3!A:A)", sheet1, workbook);

        result.Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_SheetSpan_FullRow_RecognizedAsValidReference()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        var result = _evaluator.Evaluate("=ISREF(Sheet1:Sheet3!1:1)", sheet1, workbook);

        result.Should().Be(new BoolValue(true));
    }

    // ── AST shape: the parser produces a RangeRefNode spanning the full grid extent ─────────

    [Fact]
    public void Parse_SheetSpan_FullColumn_ProducesRangeRefNodeSpanningFullRowExtent()
    {
        // Before the fix this threw FormulaParseException ("Expected cell reference after
        // 'Sheet1:Sheet3!'"). It must now parse into a RangeRefNode covering row 1..MaxRow of
        // column A on both the start and end sheet of the span.
        var ast = FormulaEvaluator.ParseFormula("SUM(Sheet1:Sheet3!A:A)");

        var call = ast.Should().BeOfType<FunctionCallNode>().Subject;
        var arg = call.Arguments.Should().ContainSingle().Subject;
        var range = arg.Should().BeOfType<RangeRefNode>().Subject;

        range.SheetName.Should().Be("Sheet1");
        range.EndSheetName.Should().Be("Sheet3");
        range.Start.ColumnName.Should().Be("A");
        range.Start.Row.Should().Be(1);
        range.End.ColumnName.Should().Be("A");
        range.End.Row.Should().Be(CellAddress.MaxRow);
    }

    [Fact]
    public void Parse_SheetSpan_FullRow_ProducesRangeRefNodeSpanningFullColumnExtent()
    {
        var ast = FormulaEvaluator.ParseFormula("SUM(Sheet1:Sheet3!1:1)");

        var call = ast.Should().BeOfType<FunctionCallNode>().Subject;
        var arg = call.Arguments.Should().ContainSingle().Subject;
        var range = arg.Should().BeOfType<RangeRefNode>().Subject;

        range.SheetName.Should().Be("Sheet1");
        range.EndSheetName.Should().Be("Sheet3");
        range.Start.Row.Should().Be(1);
        range.Start.ColumnName.Should().Be("A");
        range.End.Row.Should().Be(1);
        range.End.ColumnName.Should().Be(CellAddress.NumberToColumnName(CellAddress.MaxCol));
    }

    // ── Regression guard: plain (non-span) full-column/full-row parsing is untouched ────────

    [Fact]
    public void Parse_PlainFullColumnRange_StillProducesFullColumnRangeRefNode()
    {
        var ast = FormulaEvaluator.ParseFormula("SUM(A:A)");

        var call = ast.Should().BeOfType<FunctionCallNode>().Subject;
        var arg = call.Arguments.Should().ContainSingle().Subject;
        arg.Should().BeOfType<FullColumnRangeRefNode>();
    }

    [Fact]
    public void Sum_PlainFullColumn_StillSumsNormally()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(4));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(5));

        var result = _evaluator.Evaluate("=SUM(A:A)", sheet1, workbook);

        result.Should().Be(new NumberValue(9));
    }
}
