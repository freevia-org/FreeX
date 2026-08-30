using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R172-calc-named-lambda-call-deps: RecalcEngine.CollectReferences' generic
/// <c>case FunctionCallNode func:</c> arm walked only a call's ARGUMENTS, no matter what the callee
/// resolved to. It never considered that the callee might be a Name-Manager defined name whose
/// RefersTo is a LAMBDA -- Excel's documented "custom function via Name Manager" pattern, which
/// FormulaEvaluator.Functions.cs's EvaluateFunction genuinely supports: when the name is neither a
/// LET/LAMBDA-scoped binding, nor an AST-aware special form (LET/LAMBDA/SINGLE/ANCHORARRAY), nor a
/// built-in, it falls back to TryEvaluateNamedFormula and, if that yields a LambdaValue, invokes it
/// via InvokeLambdaWithArgs. So the LAMBDA BODY's own references are read on every evaluation, yet
/// contributed ZERO dependency edges.
///
/// Consequence: for Sheet1!B1 = "=MYCALC(A1)" with MYCALC -> "LAMBDA(n,n+Sheet2!$A$1)", editing
/// Sheet2!A1 and running an incremental <c>Recalculate</c> left B1 permanently stale -- the value
/// only ever corrected itself if something unrelated forced a full RecalculateAllFormulas.
///
/// The fix mirrors the <c>case NamedRangeNode named:</c> arm's existing treatment of a BARE named
/// formula reference: sheet-scope-first resolution, the same (name, defining-scope)
/// NamedFormulaVisitingKey cycle guard, and the same ShiftFormulaForCell/ApplyRelativeNameAnchor
/// relative-anchor handling -- resolving to PRECISE precedent cells, not a conservative boolean.
/// (The companion CF-only heuristic fix in ViewportService.ConditionalFormats.cs -- see
/// R172_CfFormulaCrossSheetLambdaCacheTests -- only decides whether to drop a colour cache and does
/// not touch the dependency graph at all, so it is not a substitute for this.)
/// </summary>
public class R172_NamedLambdaCallDependencyTests
{
    private static (RecalcEngine engine, DependencyGraph graph, Workbook wb, Sheet sheet1, Sheet sheet2) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        return (engine, graph, wb, sheet1, sheet2);
    }

    [Fact]
    public void WorkbookNamedLambdaCall_RegistersCrossSheetBodyPrecedent()
    {
        var (engine, graph, wb, sheet1, sheet2) = MakeEngine();
        wb.NamedFormulas["MYCALC"] = "LAMBDA(n,n+Sheet2!$A$1)";

        var a1S1 = new CellAddress(sheet1.Id, 1, 1);
        var a1S2 = new CellAddress(sheet2.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(a1S1, new NumberValue(1));
        sheet2.SetCell(a1S2, new NumberValue(5));
        sheet1.SetFormula(b1, "=MYCALC(A1)");

        engine.RebuildFormulaDependencies(wb);

        var precedents = graph.GetDirectPrecedents(b1);
        precedents.Should().Contain(a1S1, "the call's own argument is still a precedent");
        precedents.Should().Contain(a1S2,
            "the LAMBDA body invoked through the Name-Manager name reads Sheet2!$A$1 on every " +
            "evaluation, so it must be a registered precedent of the calling cell");
    }

    [Fact]
    public void WorkbookNamedLambdaCall_IncrementalRecalcUpdatesCallingCell()
    {
        var (engine, _, wb, sheet1, sheet2) = MakeEngine();
        wb.NamedFormulas["MYCALC"] = "LAMBDA(n,n+Sheet2!$A$1)";

        var a1S1 = new CellAddress(sheet1.Id, 1, 1);
        var a1S2 = new CellAddress(sheet2.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(a1S1, new NumberValue(1));
        sheet2.SetCell(a1S2, new NumberValue(5));
        sheet1.SetFormula(b1, "=MYCALC(A1)");

        engine.RebuildFormulaDependencies(wb);
        engine.RecalculateAllFormulas(wb);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(6));

        // The actual product-visible defect: an ordinary edit to the cell the LAMBDA body reads.
        sheet2.SetCell(a1S2, new NumberValue(50));
        engine.Recalculate(wb, [a1S2]);

        sheet1.GetValue(1, 2).Should().Be(new NumberValue(51),
            "editing the cell the named LAMBDA's body reads must dirty and recalculate the " +
            "calling cell, not leave it holding a stale value until the next full recalc");
    }

    [Fact]
    public void SheetScopedNamedLambdaCall_TakesPrecedenceOverSameNamedWorkbookLambda()
    {
        // Sheet-scope beats workbook-global for the same name, exactly as
        // Workbook.TryGetNamedFormulaText / FormulaEvaluator resolve it -- so the dependency edge
        // must land on the cell the SHEET-scoped body reads, not the global body's cell.
        var (engine, graph, wb, sheet1, sheet2) = MakeEngine();
        wb.NamedFormulas["MYCALC"] = "LAMBDA(n,n+Sheet2!$A$1)";
        wb.DefineNamedFormula("MYCALC", "LAMBDA(n,n+Sheet2!$B$5)", sheet1.Id);

        var b5S2 = new CellAddress(sheet2.Id, 5, 2);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(b5S2, new NumberValue(7));
        sheet1.SetFormula(b1, "=MYCALC(A1)");

        engine.RebuildFormulaDependencies(wb);
        graph.GetDirectPrecedents(b1).Should().Contain(b5S2);

        engine.RecalculateAllFormulas(wb);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(8));

        sheet2.SetCell(b5S2, new NumberValue(70));
        engine.Recalculate(wb, [b5S2]);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(71));
    }

    [Fact]
    public void RecursiveNamedLambdaCall_TerminatesAndStillRegistersBodyPrecedent()
    {
        // A self-recursive Name-Manager lambda (Excel's canonical FACT example) must not send the
        // dependency walk into infinite recursion -- the (name, scope) cycle guard handles it --
        // while still registering the cross-sheet cell the body reads.
        var (engine, graph, wb, sheet1, sheet2) = MakeEngine();
        wb.NamedFormulas["MYFACT"] = "LAMBDA(n,IF(n<=1,Sheet2!$A$1,n*MYFACT(n-1)))";

        var a1S2 = new CellAddress(sheet2.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(4));
        sheet2.SetCell(a1S2, new NumberValue(1));
        sheet1.SetFormula(b1, "=MYFACT(A1)");

        engine.RebuildFormulaDependencies(wb);
        graph.GetDirectPrecedents(b1).Should().Contain(a1S2);

        engine.RecalculateAllFormulas(wb);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(24));

        sheet2.SetCell(a1S2, new NumberValue(2));
        engine.Recalculate(wb, [a1S2]);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(48));
    }

    [Fact]
    public void LetBoundLambdaCall_ShadowsSameNamedWorkbookLambda_AndRegistersNoOuterBodyEdge()
    {
        // A LET binding holding a lambda is invoked by the same call syntax and wins over any
        // workbook name (EvaluateFunction checks TryResolveLambdaBinding first), so the outer
        // name's body is never read -- and must contribute no dependency edge.
        var (engine, graph, wb, sheet1, sheet2) = MakeEngine();
        wb.NamedFormulas["F"] = "LAMBDA(n,n+Sheet2!$A$1)";

        var a1S2 = new CellAddress(sheet2.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet2.SetCell(a1S2, new NumberValue(100));
        sheet1.SetFormula(b1, "=LET(F, LAMBDA(n, n*3), F(2))");

        engine.RebuildFormulaDependencies(wb);

        graph.GetDirectPrecedents(b1).Should().NotContain(a1S2,
            "the LET-bound F completely shadows the workbook name F, so the shadowed body's " +
            "Sheet2!$A$1 is never read and must not be registered as a precedent");

        engine.RecalculateAllFormulas(wb);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void UnknownFunctionName_StillWalksArgumentsOnly()
    {
        // No such defined name: the evaluator yields #NAME? and reads nothing beyond the
        // arguments, so behaviour must be unchanged from before the fix.
        var (engine, graph, wb, sheet1, _) = MakeEngine();

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(a1, new NumberValue(1));
        sheet1.SetFormula(b1, "=NOSUCHFUNC(A1)");

        engine.RebuildFormulaDependencies(wb);

        graph.GetDirectPrecedents(b1).Should().Contain(a1);
    }
}
