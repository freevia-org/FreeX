using FreeX.Core.Model;
using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for review-group O-dynamic-arrays findings H26 (bare full-column/full-row
/// dynamic-array formula must spill the used extent, not collapse to a scalar) and H27 (the A1#
/// spill-anchor operator must lex/parse/evaluate, and its anchor must be tracked as a formula
/// dependency so edits to the anchor recalc dependents).
/// </summary>
public class ODynamicArraysSpillFixesTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        return (engine, wb);
    }

    // ── H26: bare full-column/full-row dynamic-array formula spills the used extent ──────────────

    [Fact]
    public void Recalc_BareFullColumnReference_SpillsUsedExtentOfColumn()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        // Populate column A rows 1-3; row 4+ stays blank (used range ends at row 3).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        var formula = new CellAddress(sheet.Id, 1, 3); // C1 = A:A
        sheet.SetFormula(formula, "A:A");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [formula]);

        // Must spill three values (the used extent of column A), not collapse to a single scalar.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(30));
        sheet.TryGetSpillExtent(formula, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3);
        cols.Should().Be(1);
    }

    [Fact]
    public void Recalc_BareFullRowReference_SpillsUsedExtentOfRow()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        // Populate row 1 columns A-C; column D+ stays blank (used range ends at column C).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        var formula = new CellAddress(sheet.Id, 5, 1); // A5 = 1:1
        sheet.SetFormula(formula, "1:1");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [formula]);

        sheet.GetValue(5, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(3));
        sheet.TryGetSpillExtent(formula, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(1);
        cols.Should().Be(3);
    }

    [Fact]
    public void Recalc_BareFullColumnReference_BlockedBySpillTargetSetsSpillError()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));

        var formula = new CellAddress(sheet.Id, 1, 3); // C1 = A:A
        sheet.SetFormula(formula, "A:A");
        // Occupy the second spill target cell (C2) so the spill is blocked, same as any other
        // dynamic-array formula colliding with existing data.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(99));
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [formula]);

        sheet.GetValue(1, 3).Should().Be(ErrorValue.Spill);
    }

    // ── H27: A1# spill-anchor operator lexes/parses/evaluates like ANCHORARRAY(A1) ────────────────

    [Fact]
    public void Evaluate_SpillAnchorOperator_ReturnsFullSpillRangeValue()
    {
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

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=A1#", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(2));
        range.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Evaluate_SumOfSpillAnchorOperator_MatchesSumOfAnchorArrayFunction()
    {
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

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(A1#)", sheet);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Evaluate_SpillAnchorOperator_NotASpillAnchor_ReturnsRefError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=A1#", sheet);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Parse_SpillAnchorOperator_ProducesAnchorArrayFunctionCallOverTheCellRef()
    {
        var ast = FormulaEvaluator.ParseFormula("A1#");

        var call = ast.Should().BeOfType<FunctionCallNode>().Subject;
        call.FunctionName.Should().Be("ANCHORARRAY");
        call.Arguments.Should().ContainSingle().Which.Should().BeOfType<CellRefNode>()
            .Which.Should().BeEquivalentTo(new CellRefNode("A", 1));
    }

    [Fact]
    public void Parse_SpillAnchorOperator_UnexpectedAfterRangeThrowsParseException()
    {
        // '#' is only meaningful directly after a single cell reference (the spill anchor). Excel's
        // grammar does not allow it after a multi-cell range, so this must still be a parse error,
        // same as before '#' was recognized as a token at all.
        var act = () => FormulaEvaluator.ParseFormula("A1:A3#");

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void Serialize_SpillAnchorAst_RoundTripsToLiteralHashSyntax()
    {
        var ast = FormulaEvaluator.ParseFormula("A1#");

        FormulaSerializer.Serialize(ast).Should().Be("A1#");
    }

    [Fact]
    public void Rewrite_SpillAnchorFormula_ShiftsAnchorOnRowInsertAndKeepsHashSyntax()
    {
        // Inserting a row above A1 should shift the anchor reference the same way it would for any
        // other cell reference, and the rewritten formula text must still read "A2#" (not
        // "ANCHORARRAY(A2)") so a subsequent load/save round-trip stays byte-for-byte stable.
        var op = new InsertRowsOp("Sheet1", BeforeRow: 1, Count: 1);

        var rewritten = FormulaRewriter.Rewrite("A1#", op, "Sheet1");

        rewritten.Should().Be("A2#");
    }

    [Fact]
    public void Recalc_DependentFormula_RecalculatesWhenSpillAnchorSourceCellEdited()
    {
        // B1 = A1# (the spill of whatever A1 currently spills). C1 = SUM(B1) depends transitively.
        // Editing the anchor's own spilled contents (by re-running its formula) must cause B1/C1 to
        // recompute — this only works if ANCHORARRAY's argument (the CellRefNode for A1) was
        // registered as a dependency by RegisterFormulaDependencies, exactly like any other formula
        // that references A1 directly.
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(2)");

        var spillRef = new CellAddress(sheet.Id, 1, 2); // B1 = A1#
        sheet.SetFormula(spillRef, "A1#");

        var sum = new CellAddress(sheet.Id, 1, 3); // C1 = SUM(B1#)
        sheet.SetFormula(sum, "SUM(B1#)");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));

        // Now change A1's own formula so it spills different values, and recalc starting only from
        // the edited cell — the dependency graph must already know B1/C1 depend on A1 for this
        // targeted recalc to reach them.
        sheet.SetFormula(anchor, "SEQUENCE(2)+10");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(11));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(12));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(23));
    }
}
