using System;
using System.IO;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip regression tests for the §4 "Array formulas not implemented" reload bug
/// (docs/fidelity/2026-06-15-ExcelExamples1-findings.md).
///
/// After FreeX SAVES a workbook that contains dynamic-array / spill formulas, FreeX's own
/// RELOAD of the saved file (which goes through ClosedXML) must not throw. The historical
/// failure was a <c>NotImplementedException: Array formulas not implemented</c> from
/// ClosedXML's <c>SignatureAdapter.ToText</c> while reading the saved formula text.
/// </summary>
public sealed class XlsxArrayFormulaRoundTripReloadTests
{
    /// <summary>
    /// The real-world repro file from the fidelity investigation. Only runs when present so the
    /// gate stays green on machines without it; the synthetic fixtures below are the portable
    /// regression coverage.
    /// </summary>
    private const string RealReproFile = @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";

    [Fact]
    public void RealExcelExamples1_LoadSaveReload_DoesNotThrow()
    {
        if (!File.Exists(RealReproFile))
            return; // file not present on this machine — synthetic fixtures cover the regression

        // Mirror the SheetFidelity harness exactly: load → recalculate → save (fresh adapter) → reload.
        Workbook workbook;
        using (var source = File.OpenRead(RealReproFile))
            workbook = new XlsxFileAdapter().Load(source);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        Action reload = () => new XlsxFileAdapter().Load(saved);
        reload.Should().NotThrow("FreeX's own reload of a saved spill workbook must not throw");
    }

    /// <summary>
    /// Authors a workbook in memory that contains a dynamic spill formula whose text uses the
    /// modern dynamic-array functions present in the real repro file (LET / SEQUENCE), saves it,
    /// and reloads through FreeX/ClosedXML. The reload must not throw and the formula + cached
    /// values must survive.
    /// </summary>
    [Fact]
    public void AuthoredSpillFormula_LoadSaveReload_DoesNotThrowAndPreservesFormula()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateDynamicArraySpillWorkbook();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        Workbook reloaded = null!;
        Action reload = () => reloaded = adapter.Load(saved);
        reload.Should().NotThrow("FreeX's own reload of a saved spill workbook must not throw");

        var sheet = reloaded.GetSheetAt(0);
        var anchor = sheet.GetCell(3, 1)!;
        anchor.FormulaText.Should().Be("SEQUENCE(1,3)");
        anchor.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);

        // The anchor's cached top-left value must survive the round-trip (this is the value the
        // <f t="array"> cell itself carries, and the cache that stops ClosedXML recomputing on reload).
        sheet.GetValue(3, 1).Should().Be(new NumberValue(1));
    }

    /// <summary>
    /// As above but recalculates the reloaded workbook to confirm the spill still works end to end
    /// (no #SPILL!, correct values).
    /// </summary>
    [Fact]
    public void AuthoredSpillFormula_ReloadedThenRecalculated_SpillsWithoutError()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateDynamicArraySpillWorkbook();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);

        var sheet = reloaded.GetSheetAt(0);
        sheet.GetValue(3, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(3));
        sheet.GetCell(3, 1)!.Value.Should().NotBe(ErrorValue.Spill);
    }

    private static Workbook CreateDynamicArraySpillWorkbook()
    {
        var workbook = new Workbook("AuthoredSpillReload");
        var sheet = workbook.AddSheet("Data");

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");

        // Cached spill output 1,2,3 across A3:C3.
        var cells = new ScalarValue[1, 3]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3) }
        };
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));

        return workbook;
    }
}
