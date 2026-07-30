using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R92-io-defined-name-scope-eval-5-1: RecalcEngine.CollectReferences's
/// NamedRangeNode case used to resolve a NamedRangeNode purely against
/// <c>defaultSheetId</c> (the formula CELL's own sheet), completely ignoring
/// <see cref="FreeX.Core.Formula.NamedRangeNode.SheetQualifier"/> — even though
/// FormulaEvaluator.References.cs's TryResolveSheetQualifiedName already resolves the identical
/// field against the QUALIFIED sheet's own scope when actually evaluating a BARE (non-function-
/// argument) reference. When two sheets each define their own LOCAL name with the same text (e.g.
/// both Sheet1 and Sheet2 define "Data"), a sheet-qualified reference from Sheet1 to Sheet2's own
/// "Data" would register its dependency-graph edge on SHEET1's "Data" instead of Sheet2's — so an
/// edit to Sheet2's real precedent cell never dirtied the dependent formula. The fix resolves
/// SheetQualifier against the qualified sheet before consulting the scoped-formula/scoped-range/
/// global-formula tiers, exactly mirroring the evaluator's own precedence.
///
/// NOTE: the formula below deliberately uses a bare arithmetic reference ("=Sheet2!Data*2"), not
/// "=SUM(Sheet2!Data)". A sheet-qualified name used as a direct argument to an aggregate function
/// takes a COMPLETELY SEPARATE evaluation path — FormulaEvaluator.Functions.cs's NamedRangeNode
/// function-argument special case (around its ExpandArguments handling) — which itself ignores
/// NamedRangeNode.SheetQualifier entirely (only consults IsSheetScopedName/TryResolveNamedRange
/// against the formula's own current sheet). That is a genuine, separate bug in
/// FreeX.Core.Formula/FormulaEvaluator.Functions.cs, out of scope for this RecalcEngine-only fix
/// (see the round summary) — using it here would make the very first calculation already wrong,
/// for a reason unrelated to the dependency-graph defect this test targets.
/// </summary>
public class R92_DefinedNameSheetQualifierDependencyTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet1, Sheet sheet2) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        return (engine, wb, sheet1, sheet2);
    }

    [Fact]
    public void SheetQualifiedReferenceToOtherSheetsOwnLocalName_EditOnThatSheet_RecalculatesDependent()
    {
        // Sheet1 defines its OWN local "Data" = Sheet1!A1 (a distinct placeholder value, so a
        // wrong graph edge registered against THIS cell would visibly diverge from the correct
        // Sheet2-sourced result below). Sheet2 defines its OWN, separately-scoped local "Data" =
        // Sheet2!B1.
        var (engine, wb, sheet1, sheet2) = MakeEngine();

        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s2b1 = new CellAddress(sheet2.Id, 1, 2);
        sheet1.SetCell(s1a1, new NumberValue(1005));
        sheet2.SetCell(s2b1, new NumberValue(10));

        wb.DefineNamedRange("Data", new GridRange(s1a1, s1a1), metadata: null, scopeSheetId: sheet1.Id);
        wb.DefineNamedRange("Data", new GridRange(s2b1, s2b1), metadata: null, scopeSheetId: sheet2.Id);

        // C1 on Sheet1 explicitly qualifies the reference to Sheet2's OWN local "Data". A bare
        // reference (not a direct SUM(...) argument) so it goes through
        // FormulaEvaluator.References.cs's EvaluateNamedRange/TryResolveSheetQualifiedName path,
        // which already resolves SheetQualifier correctly -- isolating this test to the
        // RecalcEngine dependency-graph defect (see the class remarks re: the separate
        // FormulaEvaluator.Functions.cs aggregate-argument bug).
        var c1 = new CellAddress(sheet1.Id, 1, 3);
        sheet1.SetFormula(c1, "=Sheet2!Data");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [c1]);

        sheet1.GetValue(1, 3).Should().Be(new NumberValue(10),
            "the evaluator already resolves the sheet-qualified name correctly on first calculation");

        // Edit Sheet2!B1 -- C1's REAL precedent. Only [b1] is passed as the changed cell, so this
        // relies entirely on the dependency-graph edge (not a full recalc) to dirty C1.
        sheet2.SetCell(s2b1, new NumberValue(300));
        engine.Recalculate(wb, [s2b1]);

        sheet1.GetValue(1, 3).Should().Be(new NumberValue(300),
            "C1 depends on Sheet2's own local Data (B1), so editing B1 must dirty C1 through the " +
            "graph edge, not register the edge against Sheet1's unrelated same-named local Data");
    }

    [Fact]
    public void UnqualifiedSheetScopedName_StillResolvesAgainstFormulaCellsOwnSheet()
    {
        // No-regression sibling: an UNqualified reference to a sheet-scoped name (the common case,
        // handled by the pre-existing defaultSheetId path) must keep resolving against the FORMULA
        // cell's own sheet, unaffected by the new SheetQualifier-first branch.
        var (engine, wb, sheet1, sheet2) = MakeEngine();

        for (var r = 1u; r <= 5; r++)
        {
            sheet1.SetCell(new CellAddress(sheet1.Id, r, 1), new NumberValue(1000 + r));
            sheet2.SetCell(new CellAddress(sheet2.Id, r, 2), new NumberValue(r * 10)); // 150
        }

        wb.DefineNamedRange("Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)),
            metadata: null, scopeSheetId: sheet1.Id);
        wb.DefineNamedRange("Data",
            new GridRange(new CellAddress(sheet2.Id, 1, 2), new CellAddress(sheet2.Id, 5, 2)),
            metadata: null, scopeSheetId: sheet2.Id);

        // D1 on Sheet2 references its OWN local "Data" without any sheet qualifier.
        var d1 = new CellAddress(sheet2.Id, 1, 4);
        sheet2.SetFormula(d1, "=SUM(Data)");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [d1]);

        sheet2.GetValue(1, 4).Should().Be(new NumberValue(150));

        var b3 = new CellAddress(sheet2.Id, 3, 2);
        sheet2.SetCell(b3, new NumberValue(300));
        engine.Recalculate(wb, [b3]);

        sheet2.GetValue(1, 4).Should().Be(new NumberValue(420),
            "unqualified sheet-scoped name resolution must keep working exactly as before this fix");
    }
}
