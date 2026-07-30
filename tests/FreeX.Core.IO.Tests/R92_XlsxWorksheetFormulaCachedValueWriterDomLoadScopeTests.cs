using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R92-app-large-workbook-memory-5-1: <see cref="XlsxWorksheetFormulaCachedValueWriter.Save"/> used to
/// DOM-load (via <c>XlsxWorksheetXmlEditSession.TryGetWorksheet</c> -&gt; full <c>XDocument</c> parse
/// of the entire worksheet part) EVERY sheet in the workbook whenever ANY sheet anywhere had a
/// formula -- the gate that reaches this method (<c>XlsxPostProcessingFeaturePlan.HasCellFormulas</c>)
/// is workbook-wide, not per-sheet. On a workbook where one sheet has a multi-hundred-MB grid of plain
/// values and an unrelated sheet has a formula, the plain-value sheet paid a full DOM parse + per-cell
/// walk for nothing: nothing on that sheet could ever need a cached-value patch.
///
/// These tests use the internal <see cref="XlsxWorksheetFormulaCachedValueWriter.DiagnosticsWorksheetLoadAttempts"/>
/// counter (a test-only seam) rather than wall-clock timing to assert exactly which sheets the writer
/// actually attempts to DOM-load, driven through the real production entry point
/// (<see cref="XlsxFileAdapter.Save"/>) on a freshly-authored (no source package) workbook, which is
/// the only path that reaches this writer exactly once per save.
/// </summary>
public sealed class R92_XlsxWorksheetFormulaCachedValueWriterDomLoadScopeTests
{
    [Fact]
    public void Save_SheetWithNoFormulasOrSpills_IsNeverDomLoaded()
    {
        var workbook = new Workbook("MixedWorkbook");

        var formulaSheet = workbook.AddSheet("HasFormula");
        var formulaAddress = new CellAddress(formulaSheet.Id, 1, 1);
        formulaSheet.SetFormula(formulaAddress, "1+1");
        formulaSheet.GetCell(formulaAddress)!.Value = new NumberValue(2);

        var plainSheet = workbook.AddSheet("PlainValues");
        plainSheet.SetCell(new CellAddress(plainSheet.Id, 1, 1), new NumberValue(42));
        plainSheet.SetCell(new CellAddress(plainSheet.Id, 2, 1), new TextValue("hello"));

        XlsxWorksheetFormulaCachedValueWriter.ResetDiagnosticsForTests();

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Only "HasFormula" should ever have been considered for a DOM load -- "PlainValues" has
        // neither a formula cell nor a live spill value, so nothing on it could possibly need a
        // cached-value patch.
        XlsxWorksheetFormulaCachedValueWriter.DiagnosticsWorksheetLoadAttempts.Should().Be(1,
            "the plain-values sheet has no formulas/spills and must never be DOM-loaded by this writer");
    }

    [Fact]
    public void Save_AllSheetsHaveFormulas_EveryOneIsStillLoaded()
    {
        // No-regression sibling: when every sheet genuinely needs the cached-value pass, the writer
        // must still visit every one of them -- the new per-sheet skip must not become an
        // over-broad skip that silently drops legitimate work.
        var workbook = new Workbook("AllFormulaWorkbook");

        var sheetOne = workbook.AddSheet("Sheet1");
        var addressOne = new CellAddress(sheetOne.Id, 1, 1);
        sheetOne.SetFormula(addressOne, "1+1");
        sheetOne.GetCell(addressOne)!.Value = new NumberValue(2);

        var sheetTwo = workbook.AddSheet("Sheet2");
        var addressTwo = new CellAddress(sheetTwo.Id, 1, 1);
        sheetTwo.SetFormula(addressTwo, "2+2");
        sheetTwo.GetCell(addressTwo)!.Value = new NumberValue(4);

        XlsxWorksheetFormulaCachedValueWriter.ResetDiagnosticsForTests();

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        XlsxWorksheetFormulaCachedValueWriter.DiagnosticsWorksheetLoadAttempts.Should().Be(2,
            "both sheets have a formula cell, so both must still be visited for the cached-value pass");
    }
}
