using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for R111-calc-union-intersection-endpoint-deps: RecalcEngine.CollectReferences
/// had no case arm for UnionNode, IntersectionNode, or NamedRangeEndpointNode -- the AST node kinds
/// FormulaEvaluator.cs uses (EvaluateUnionNode/EvaluateIntersectionNode/EvaluateNamedRangeEndpointNode)
/// for a parenthesized multi-area union (e.g. "=SUM((A1:A5,C1:C5))", added R93 -- see
/// R93_AreasUnionValueModelTests), a space-intersection, or an INDEX(...)-anchored range endpoint.
/// Falling into one of these three node kinds with no matching case hit the bare `return false`
/// after the switch, contributing zero dependency edges and zero volatility signal -- so a plain
/// precedent reachable only through a union/intersection never dirtied its dependent (stale cached
/// value forever), and a volatile function nested inside one was never added to _volatileCells.
///
/// This is the same defect class R29/R92 already fixed for ANCHORARRAY's implicit union rectangle,
/// one AST level higher, for the newer (R93-added) UnionNode/IntersectionNode/NamedRangeEndpointNode
/// node kinds.
/// </summary>
public class R111_UnionIntersectionEndpointDependencyTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        return (engine, wb, sheet);
    }

    [Fact]
    public void CellInsideUnionArea_Edit_RecalculatesDependentFormula()
    {
        // E1 = SUM((A1:A1,C1:C1)) -- a UnionNode as a direct FunctionCallNode argument (R93 syntax).
        // Before the fix: editing A1 never marked E1 dirty because CollectReferences never
        // recursed into the UnionNode, so neither A1 nor C1 got a dependency edge.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(c1, new NumberValue(20));
        sheet.SetFormula(e1, "=SUM((A1:A1,C1:C1))");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, c1, e1]);
        sheet.GetValue(1, 5).Should().Be(new NumberValue(30), "initial SUM((A1:A1,C1:C1)) = 10 + 20");

        sheet.SetCell(a1, new NumberValue(1000));
        var report = engine.Recalculate(wb, [a1]);

        report.RecalculatedCells.Should().Contain(e1,
            "editing A1 (inside the first union area) must mark E1 dirty and recalculate it");
        sheet.GetValue(1, 5).Should().Be(new NumberValue(1020),
            "Excel recalculates SUM((A1:A1,C1:C1)) to 1000 + 20 = 1020 immediately on the A1 edit");
    }

    [Fact]
    public void CellInsideSecondUnionArea_Edit_RecalculatesDependentFormula()
    {
        // Sibling coverage: the SECOND area of the union (C1) must also get a dependency edge, not
        // just the first -- guards against a fix that only recurses into Areas[0].
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(c1, new NumberValue(20));
        sheet.SetFormula(e1, "=SUM((A1:A1,C1:C1))");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, c1, e1]);

        sheet.SetCell(c1, new NumberValue(500));
        var report = engine.Recalculate(wb, [c1]);

        report.RecalculatedCells.Should().Contain(e1,
            "editing C1 (inside the second union area) must mark E1 dirty and recalculate it");
        sheet.GetValue(1, 5).Should().Be(new NumberValue(510));
    }

    [Fact]
    public void VolatileFunctionInsideUnionArea_MarksDependentVolatile_RecalculatesEveryPass()
    {
        // E1 = SUM((OFFSET($A$1,0,0),$C$1)) -- a volatile OFFSET call nested inside a UnionNode as
        // one of its Areas (the parser accepts an arbitrary expression per union area -- see
        // Parser.cs's `areas.Add(ParseExpression())`). Before this fix: CollectReferences never
        // recursed into the UnionNode at all, so OFFSET's volatility never propagated up to E1 --
        // E1 was never added to _volatileCells and never re-evaluated on a subsequent recalc pass
        // the way a bare "=OFFSET(...)" cell would be.
        //
        // Note: FormulaEvaluator's own union-area evaluation (EvaluateUnionNode/EvaluateArrayOperand)
        // has a separate, pre-existing gap -- it doesn't resolve OFFSET/INDIRECT/INDEX/CHOOSE to a
        // reference when they appear as a bare union area, so this SUM's own computed value is
        // #VALUE! regardless of this fix (see FormulaEvaluator.References.cs; out of scope for this
        // RecalcEngine-only fix -- flagged as a sibling lead). That is orthogonal to what this test
        // proves: dependency registration (CollectReferences) runs at formula-registration time
        // independent of whether evaluation later succeeds, so the volatility signal this fix adds
        // is fully exercised and observable via RecalculatedCells regardless of the SUM's own error.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(c1, new NumberValue(2));
        sheet.SetFormula(e1, "=SUM((OFFSET($A$1,0,0),$C$1))");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, c1, e1]);

        // Change a cell utterly unrelated to E1's dependency edges, forcing a "volatile-only" pass:
        // a bare full recalc with no edited precedent of E1 at all should still re-run E1 purely
        // because it is volatile (mirrors how NOW()/RAND()/OFFSET() cells recalc on every F9).
        var report = engine.Recalculate(wb, []);

        report.RecalculatedCells.Should().Contain(e1,
            "a volatile function nested inside a union area must mark the whole formula volatile, " +
            "so it re-evaluates on every recalc pass even with no explicitly-edited precedent");
    }

    [Fact]
    public void IntersectionNode_CellInEitherOperand_Edit_RecalculatesDependentFormula()
    {
        // E1 = SUM(A1:B5 A1:A10) -- a space-intersection reference (IntersectionNode). Recursing
        // into both operands (this fix) means a cell inside EITHER operand's literal range gets a
        // dependency edge, even though true dependency tracking would ideally track only the
        // intersected rectangle (A1:A5) -- see fix comment for the documented tradeoff.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetCell(a1, new NumberValue(7));
        sheet.SetFormula(e1, "=SUM(A1:B5 A1:A10)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, e1]);
        sheet.GetValue(1, 5).Should().Be(new NumberValue(7));

        sheet.SetCell(a1, new NumberValue(99));
        var report = engine.Recalculate(wb, [a1]);

        report.RecalculatedCells.Should().Contain(e1,
            "editing A1 (inside both intersection operands) must mark E1 dirty and recalculate it");
        sheet.GetValue(1, 5).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void NamedRangeEndpointNode_StartCellEdit_RecalculatesDependentFormula()
    {
        // E1 = SUM(A1:EndName), where EndName is a defined name pointing at C1 (a single cell) --
        // Excel's "A1:aDefinedName" shape, parsed to NamedRangeEndpointNode(Start=A1 (CellRefNode),
        // End=NamedRangeNode("EndName")). Before the fix, NamedRangeEndpointNode had no case at all
        // (not even falling back on the already-working plain NamedRangeNode handling), so the
        // Start endpoint (A1) never got a dependency edge -- editing it never dirtied E1.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        wb.DefineNamedRange("EndName", new GridRange(c1, c1));
        sheet.SetCell(a1, new NumberValue(4));
        sheet.SetCell(c1, new NumberValue(6));
        sheet.SetFormula(e1, "=SUM(A1:EndName)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, c1, e1]);
        sheet.GetValue(1, 5).Should().Be(new NumberValue(10), "initial SUM(A1:C1) = 4 + (implicit B1=0) + 6");

        sheet.SetCell(a1, new NumberValue(400));
        var report = engine.Recalculate(wb, [a1]);

        report.RecalculatedCells.Should().Contain(e1,
            "editing A1 (the NamedRangeEndpointNode's Start endpoint) must mark E1 dirty and recalculate it");
        sheet.GetValue(1, 5).Should().Be(new NumberValue(406));
    }
}
