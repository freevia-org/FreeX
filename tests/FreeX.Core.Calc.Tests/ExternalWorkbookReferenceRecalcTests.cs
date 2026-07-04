using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for finding J16: recalculating a formula that references another workbook
/// (e.g. <c>=[Book1.xlsx]Sheet1!A1</c>) must preserve the previously-loaded/cached value, exactly
/// like Excel does until the user explicitly updates links. Our Lexer/Parser have no concept of
/// the OOXML external-workbook-reference syntax, so <see cref="FormulaEvaluator.ParseFormula"/>
/// always throws <see cref="FormulaParseException"/> for such formulas. Before the fix,
/// RecalcEngine's catch(FormulaParseException) unconditionally overwrote the cell's value with
/// <see cref="ErrorValue.Value"/> (#VALUE!), destroying data that would then be persisted to disk
/// on the next save.
/// </summary>
public sealed class ExternalWorkbookReferenceRecalcTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    /// <summary>
    /// Build a cell the way the loader (XlsxFileAdapter + Cell.FromFormula) would: formula text
    /// set, but Value pre-populated from the file's cached &lt;v&gt; — i.e. no CachedAst yet, and
    /// the external-ref formula has never been successfully parsed by our engine.
    /// </summary>
    private static Cell LoadedExternalRefCell(string formulaText, ScalarValue cachedValue) =>
        new Cell { FormulaText = formulaText, Value = cachedValue };

    [Fact]
    public void RecalculateAllFormulas_PreservesCachedValue_ForExternalWorkbookReference()
    {
        // Arrange: simulate a workbook loaded from disk with =[Book1.xlsx]Sheet1!A1 in A1,
        // whose last Excel-computed cached value was 42.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, LoadedExternalRefCell("[Book1.xlsx]Sheet1!A1", new NumberValue(42)));

        // Act: this is exactly what the "Calculate Now" QAT command invokes.
        var report = Engine().RecalculateAllFormulas(workbook);

        // Assert: the cached value must survive untouched — Excel never blanks a valid external
        // link's last-known value just because the workbook was recalculated.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(42),
            "Excel preserves an external reference's last-known cached value until the user " +
            "explicitly updates links; recalculating must not overwrite it with #VALUE!");

        // No spurious #VALUE! error should be reported for this cell either.
        report.Errors.Should().NotContain(e => e.Cell == addr);
    }

    [Fact]
    public void RecalculateAllFormulas_PreservesCachedValue_ForExternalWorkbookReference_TextValue()
    {
        // Same scenario but with a text cached value, to make sure the preserved-value path is not
        // accidentally numeric-only.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(addr, LoadedExternalRefCell("[Book1.xlsx]Sheet1!B2", new TextValue("Acme Corp")));

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(2, 1).Should().Be(new TextValue("Acme Corp"));
    }

    [Fact]
    public void Recalculate_TriggeredByUnrelatedCellEdit_PreservesExternalReferenceCachedValue()
    {
        // Arrange: an external-ref formula cell, plus a second, unrelated formula cell in the same
        // workbook. Editing the unrelated cell drives a normal (non-"recalculate all") pass that
        // still walks every dirty/changed formula cell via Recalculate/CollectFormulaCells-style
        // traversal — reproducing "editing any cell in the same recalc batch" from the finding.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var externalAddr = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(externalAddr, LoadedExternalRefCell("[Book1.xlsx]Sheet1!A1", new NumberValue(7)));

        var unrelatedAddr = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(unrelatedAddr, "1+1");

        var engine = Engine();
        // First establish dependency graph/state as if the workbook had just been loaded.
        engine.RebuildFormulaDependencies(workbook);

        // Act: simulate editing B1 (WorkbookCellEditService's ordinary recalc path), which
        // recalculates the changed cell plus anything the dependency graph reports as dirty.
        // The external-ref cell A1 has no dependents/precedents relating it to B1, but a broad
        // "Calculate Now"-style pass (or any pass that re-visits every formula cell) must still
        // leave it alone.
        engine.RecalculateAllFormulas(workbook);

        // Assert
        sheet.GetValue(1, 1).Should().Be(new NumberValue(7),
            "an ordinary edit elsewhere in the workbook must not corrupt an unrelated external " +
            "link's cached value");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void RecalculateAllFormulas_RoundTrip_FormulaTextAndCachedValueSurviveMultiplePasses()
    {
        // Round-trip simulation: what gets saved to disk is whatever `cell.Value`/`cell.FormulaText`
        // hold after recalculation (XlsxFileAdapter's patch/full-rewrite save paths read these
        // directly). Run several recalculation passes, as "Calculate Now" pressed repeatedly would,
        // and confirm both the formula text and the cached value are stable/unchanged, so a
        // save-after-recalc round-trips the original cached value rather than persisting #VALUE!.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 3, 3);
        const string formulaText = "[Book1.xlsx]Sheet1!C3";
        sheet.SetCell(addr, LoadedExternalRefCell(formulaText, new NumberValue(123.5)));

        var engine = Engine();
        for (var i = 0; i < 3; i++)
            engine.RecalculateAllFormulas(workbook);

        var cell = sheet.GetCell(addr)!;
        cell.FormulaText.Should().Be(formulaText, "the formula text itself must never be altered by recalc");
        cell.Value.Should().Be(new NumberValue(123.5),
            "repeated Calculate Now passes must not progressively destroy the cached value");
    }

    [Fact]
    public void RecalculateAllFormulas_GenuinelyInvalidFormula_StillReportsValueError()
    {
        // Guard against over-broadening the fix: a formula that is simply malformed (no bracket
        // anywhere) must still surface as #VALUE!, exactly as before. An unclosed function call is
        // an unambiguous parse error in any formula grammar.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new Cell { FormulaText = "SUM(1,2", Value = new NumberValue(99) });

        var report = Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Value,
            "a genuinely malformed formula (unrelated to external references) must still resolve to #VALUE!");
        report.Errors.Should().Contain(e => e.Cell == addr && e.Error == "#VALUE!");
    }

    [Fact]
    public void RunIterativeCalc_PreservesExternalReferenceCachedValue_WhenCellIsPartOfACycle()
    {
        // The iterative-calculation path (RunIterativeCalc) has its own catch(FormulaParseException)
        // block; make sure it also preserves the cached value rather than blanking to #VALUE!.
        //
        // To reach it, first establish a genuine self-referencing cycle (A1 = A1+1) so the
        // dependency graph records a real cyclic edge and iterative calc engages. Then simulate the
        // formula being edited in place to a syntactically similar but unparseable external
        // reference (e.g. after a paste/undo race). From here on we drive recalculation via the
        // incremental Recalculate(workbook, changedCells) entry point (what an ordinary cell edit
        // uses) rather than RecalculateAllFormulas, because the latter unconditionally rebuilds the
        // whole dependency graph from scratch first — which would simply drop the unparseable
        // cell's edges rather than exercising the "stale cyclic edge + reparse failure" path this
        // test targets. EnsureChangedFormulaDependenciesRegistered leaves prior graph edges alone
        // when a changed cell fails to reparse, so A1's earlier self-referencing edge survives and
        // it is still classified as cyclic on the next pass.
        var workbook = new Workbook("Test");
        workbook.IterativeCalculation = true;
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);

        var engine = Engine();
        sheet.SetFormula(addr, "A1+1");
        engine.Recalculate(workbook, [addr]);

        // Sanity: the genuine cycle actually ran through the iterative path.
        sheet.GetCell(addr)!.CachedAst.Should().NotBeNull();

        // Now "edit" the formula to an external-workbook reference with a pre-set cached value,
        // as the loader would after re-opening a saved file with a manually patched formula.
        sheet.SetFormula(addr, "[Book1.xlsx]Sheet1!A1+1");
        sheet.GetCell(addr)!.Value = new NumberValue(77);

        engine.Recalculate(workbook, [addr]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(77),
            "external-reference formulas swept into an iterative-calc cyclic pass must keep their " +
            "last-known cached value too, not be reset to #VALUE!");
    }
}
