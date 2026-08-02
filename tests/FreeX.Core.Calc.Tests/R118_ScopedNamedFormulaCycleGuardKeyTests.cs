using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R118-calc-named-formula-scope-key: RecalcEngine.CollectReferences's recursion guard against
/// name-to-name reference cycles (<c>namedFormulaStack</c>) was keyed by the BARE defined-name
/// text instead of (name, defining-scope) the way FormulaEvaluator's identical-purpose guard
/// (NamedFormulaVisitingKey, see FormulaEvaluator.References.cs and the passing R50-meta-2 test)
/// already is. Two DIFFERENT sheet-scoped named formulas that happen to share a bare name (e.g.
/// Sheet1's own "Foo" and Sheet2's own "Foo") therefore falsely collided in the DEPENDENCY-GRAPH
/// builder whenever one referenced the other via an explicit sheet qualifier: the second (inner)
/// encounter of the bare name failed the HashSet.Add guard and the method bailed out immediately,
/// silently dropping every cell reference reachable only through that inner definition from the
/// dependency graph. The evaluator's own answer was (and remains) correct -- only the graph used
/// for dirty-propagation/recalc-ordering was wrong -- so a full recalc always looked right and the
/// bug only surfaced as a STALE cached value after an ordinary edit to the precedent cell reached
/// solely through the inner name.
/// </summary>
public sealed class R118_ScopedNamedFormulaCycleGuardKeyTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    // ── R118-calc-named-formula-scope-key: distinct same-named scoped formulas must not
    //    collide in the dependency graph, so editing the transitively-referenced precedent cell
    //    correctly dirties the dependent cell on the NEXT incremental recalc. ──────────────────

    [Fact]
    public void EditingPrecedentCellReachedThroughDistinctSameNamedScopedFormula_DirtiesDependentCell()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1); // Sheet2!A1
        sheet2.SetCell(sheet2A1, new NumberValue(5));

        // Two DIFFERENT named formulas that both happen to be called "Foo", each scoped to its own
        // sheet: Sheet1's "Foo" refers to Sheet2's own (distinct) "Foo", which in turn refers to
        // Sheet2's own A1 -- exactly the R118 finding's scenario. Sheet2's own definition
        // explicitly self-qualifies the cell ref (as Excel always stores an absolute RefersTo for a
        // sheet-scoped name) so the assertion is unambiguous regardless of which sheet is "current"
        // during evaluation.
        workbook.DefineNamedFormula("Foo", "Sheet2!Foo+1", sheet1.Id);
        workbook.DefineNamedFormula("Foo", "Sheet2!$A$1", sheet2.Id);

        var b1 = new CellAddress(sheet1.Id, 1, 2); // Sheet1!B1
        sheet1.SetFormula(b1, "Foo");

        var engine = Engine();
        engine.RegisterFormulaDependencies(b1, FormulaEvaluator.ParseFormula("Foo"), sheet1.Id, workbook);
        engine.Recalculate(workbook, [b1]);

        sheet1.GetCell(b1)!.Value.Should().Be(new NumberValue(6),
            "Sheet2!Foo resolves to Sheet2!A1 (5), so Sheet1!Foo = 5 + 1 = 6");

        // Act: edit ONLY the precedent cell (Sheet2!A1), reached transitively through the two
        // distinct same-named scoped formulas, and recalc incrementally (the real edit path) --
        // never re-registering B1's dependencies again.
        sheet2.SetCell(sheet2A1, new NumberValue(100));
        engine.Recalculate(workbook, [sheet2A1]);

        // Assert: with the dependency edge correctly registered, B1 must be dirtied and
        // recalculated to reflect the new precedent value.
        sheet1.GetCell(b1)!.Value.Should().Be(new NumberValue(101),
            "the dependency graph must have an edge from Sheet2!A1 to Sheet1!B1 through the two " +
            "distinct scoped 'Foo' definitions -- editing A1 must dirty and recalculate B1, not " +
            "leave it showing the stale pre-edit value of 6");
    }

    // ── No-regression sibling: a GENUINE cycle through two distinct scoped formulas of the same
    //    name must still be caught by the guard (same (name, scope) pair re-entered), so building
    //    the dependency graph terminates instead of recursing forever / stack-overflowing. ──────

    [Fact]
    public void GenuineCycleAcrossTwoDistinctSameNamedScopedFormulas_DependencyGraphBuildTerminates()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // A genuine cycle: Sheet1!Foo -> Sheet2!Foo -> Sheet1!Foo (re-entering the SAME (Foo,
        // Sheet1) definition it started from).
        workbook.DefineNamedFormula("Foo", "Sheet2!Foo+1", sheet1.Id);
        workbook.DefineNamedFormula("Foo", "Sheet1!Foo+1", sheet2.Id);

        var b1 = new CellAddress(sheet1.Id, 1, 2); // Sheet1!B1
        sheet1.SetFormula(b1, "Foo");

        var engine = Engine();

        // Must not hang or stack-overflow: the guard still stops re-entry into the identical
        // (name, scope) definition, it just no longer conflates that with a DIFFERENT scope
        // sharing the same bare text.
        var act = () => engine.RegisterFormulaDependencies(
            b1, FormulaEvaluator.ParseFormula("Foo"), sheet1.Id, workbook);
        act.Should().NotThrow("the cycle guard must still catch genuine re-entry into the same " +
            "(name, scope) definition and terminate the walk");

        engine.Recalculate(workbook, [b1]);

        // The evaluator independently returns #REF! for this genuine cycle (see the matching
        // R50_FormulaFindingsTests sibling); the dependency-graph build merely must not hang, which
        // the NotThrow assertion above already proves.
    }
}
