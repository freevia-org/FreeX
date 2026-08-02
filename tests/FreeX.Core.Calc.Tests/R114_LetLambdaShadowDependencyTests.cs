using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R114-calc-let-lambda-shadow: RecalcEngine.CollectReferences had no concept
/// of LET/LAMBDA lexical scoping at all. A LET binding name (or LAMBDA parameter name) is parsed as
/// a plain <see cref="NamedRangeNode"/> -- see FormulaEvaluator.LocalScopes.cs's EvaluateLet/
/// EvaluateLambda -- and the generic <c>case FunctionCallNode func:</c> arm walked every argument of
/// every function call uniformly, including LET's/LAMBDA's binding-name slots, feeding them into
/// <c>case NamedRangeNode named:</c> which unconditionally resolved the identifier against
/// workbook.TryGetNamedRange/TryGetNamedFormulaText whenever a same-named workbook/sheet-scoped
/// defined name happened to exist -- even though the evaluator's own EvaluateNamedRange
/// (FormulaEvaluator.References.cs) checks context.TryResolveLambdaBinding(node.Name) BEFORE ever
/// consulting the workbook's named ranges, so the shadowed outer name is never actually read. This
/// registered a bogus dependency edge -- including a false SELF-loop when the shadowed name
/// happens to refer back to the formula's own cell, which the cycle-detection machinery then
/// treats as a genuine circular reference (see RecalcEngine.cs's SUBTOTAL/AGGREGATE self-exclusion
/// comments for the same self-loop-means-circular contract).
/// </summary>
public class R114_LetLambdaShadowDependencyTests
{
    private static (RecalcEngine engine, DependencyGraph graph, Workbook wb, Sheet sheet) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        return (engine, graph, wb, sheet);
    }

    [Fact]
    public void LetBindingShadowingSelfReferencingName_DoesNotRegisterSelfLoop_AndEvaluatesCorrectly()
    {
        // Workbook-scoped name "n" -> Sheet1!$A$1 (the SAME cell the LET formula lives in). Excel:
        // =LET(n, 3, n*2) in A1 evaluates to 6 with no circular-reference indication whatsoever --
        // LET's own binding of "n" completely shadows the outer name for the whole body.
        var (engine, graph, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        wb.DefineNamedRange("n", new GridRange(a1, a1));

        sheet.SetFormula(a1, "=LET(n, 3, n*2)");

        engine.RebuildFormulaDependencies(wb);

        // The core defect: no self-loop dependency edge should ever be registered for A1.
        graph.GetDirectPrecedents(a1).Should().NotContain(a1,
            "LET's own binding of \"n\" shadows the workbook name \"n\" -> A1 for the whole body, " +
            "so the formula never actually reads A1 and must not depend on itself");

        var report = engine.Recalculate(wb, [a1]);

        report.CyclicCells.Should().BeEmpty(
            "a shadowed LET binding must never be classified as a circular reference");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(6),
                "Excel evaluates LET(n,3,n*2) to 6, with the LET-local n completely shadowing the " +
                "outer named range n -> A1");
    }

    [Fact]
    public void LetBindingShadowingUnrelatedName_DoesNotRegisterBogusDependencyEdge()
    {
        // Workbook-scoped name "n" -> B1 (some OTHER cell, not a self-loop this time). The LET
        // formula in A1 never actually reads B1 (its own "n" shadows the outer name), so editing
        // B1 must NOT recalculate A1 -- there is no real dependency to track.
        var (engine, graph, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(100));
        wb.DefineNamedRange("n", new GridRange(b1, b1));

        sheet.SetFormula(a1, "=LET(n, 3, n*2)");

        engine.RebuildFormulaDependencies(wb);

        graph.GetDirectPrecedents(a1).Should().NotContain(b1,
            "the LET-local n shadows the outer name n -> B1 for the whole body, so the formula " +
            "never reads B1 and must not register a dependency edge on it");

        engine.Recalculate(wb, [a1]);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(6));

        // Edit the coincidentally-same-named cell; A1 must be untouched by a real recalc pass
        // driven purely by the (correctly empty) dependency graph.
        sheet.SetCell(b1, new NumberValue(999));
        var report = engine.Recalculate(wb, [b1]);

        report.RecalculatedCells.Should().NotContain(a1,
            "A1 has no real dependency on B1 once the LET shadow is honoured, so changing B1 must " +
            "not recalculate A1");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(6),
            "A1's value must be unaffected by an edit to the unrelated, merely same-named, B1");
    }

    [Fact]
    public void LambdaParameterShadowingSelfReferencingName_DoesNotRegisterSelfLoop()
    {
        // Same shadowing contract for LAMBDA parameters: a workbook name "x" -> A1, and A1's own
        // formula defines-and-immediately-invokes a LAMBDA whose parameter is also named "x".
        // Excel: =LAMBDA(x, x*2)(5) evaluates to 10, with the LAMBDA's own parameter "x" shadowing
        // the outer named range "x" for the whole body -- no dependency on A1 itself.
        var (engine, graph, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        wb.DefineNamedRange("x", new GridRange(a1, a1));

        sheet.SetFormula(a1, "=LAMBDA(x, x*2)(5)");

        engine.RebuildFormulaDependencies(wb);

        graph.GetDirectPrecedents(a1).Should().NotContain(a1,
            "LAMBDA's own parameter \"x\" shadows the workbook name \"x\" -> A1 for the whole body");

        var report = engine.Recalculate(wb, [a1]);

        report.CyclicCells.Should().BeEmpty(
            "a shadowed LAMBDA parameter must never be classified as a circular reference");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(10),
            "Excel evaluates LAMBDA(x,x*2)(5) to 10, with the LAMBDA-local x completely shadowing " +
            "the outer named range x -> A1");
    }

    [Fact]
    public void UnshadowedNamedRangeInsideLet_StillRegistersRealDependencyEdge()
    {
        // No-regression sibling: a LET body that references a workbook name NOT used as one of its
        // own binding names must still track a REAL dependency edge and recalculate correctly --
        // the shadow check must not over-broadly suppress genuine named-range dependencies.
        var (engine, graph, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(10));
        wb.DefineNamedRange("Other", new GridRange(b1, b1));

        // "n" is LET-local; "Other" is a genuine, unshadowed workbook name read inside the body.
        sheet.SetFormula(a1, "=LET(n, 3, n + Other)");

        engine.RebuildFormulaDependencies(wb);

        graph.GetDirectPrecedents(a1).Should().Contain(b1,
            "Other is never shadowed by any LET binding here, so the formula genuinely depends on B1");

        engine.Recalculate(wb, [a1]);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(13));

        sheet.SetCell(b1, new NumberValue(50));
        engine.Recalculate(wb, [b1]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(53),
            "editing B1 (the real, unshadowed precedent) must still recalculate A1 through the " +
            "dependency graph edge");
    }
}
