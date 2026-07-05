using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

// Regression coverage for K28: the round-4 J28 fix (XlsxClosedXmlCellMapperErrorRoundTripTests)
// only proves that a plain, non-formula VALUE cell serializes #SPILL!/#CALC! as t="str" text
// instead of silently downgrading to #N/A (via XlsxClosedXmlCellMapper.MapValueInverse). It never
// exercises the path that FORMULA cells actually take in production: a formula cell's cached
// error is written by XlsxWorksheetFormulaCachedValueWriter.WriteCachedValue — raw XML tree
// surgery performed AFTER ClosedXML's own save — which bypasses MapValueInverse entirely.
//
// These tests save a real FORMULA cell (via the full XlsxFileAdapter Save pipeline, the same one
// driven by every production save) carrying each of #SPILL!/#CALC!/#CIRCULAR! as its live cached
// value, then reload it through the full Load pipeline and assert the round-trip matches real
// Excel behavior:
//   - #SPILL!/#CALC! ARE valid OOXML error codes Excel writes verbatim as t="e" — and although
//     ClosedXML's own XLError enum can't represent them (XLCell.Value/MapFormulaValue returns
//     BlankValue for such a cell), XlsxWorksheetCellLayoutReader.ReadCachedFormulaErrors raw-parses
//     the <c t="e"><f/><v>#SPILL!</v></c> XML directly and XlsxFileAdapter falls back to it, so the
//     cell must reload as the exact same error — never #N/A.
//   - #CIRCULAR! is a FreeX-only sentinel (RecalcEngine.AddCyclicCell), not a real OOXML error code
//     at all. Real Excel never writes it: with iterative calculation off, Excel persists a plain 0
//     for a non-iterative circular reference. The formula-cached writer must match that (mirroring
//     MapValueInverse's identical decision for the non-formula path) instead of letting it round-trip
//     as a bogus ErrorValue("#CIRCULAR!") via the raw-XML fallback reader's catch-all mapping.
public sealed class XlsxFormulaCachedErrorRoundTripTests
{
    private static Workbook CreateWorkbookWithFormulaCachedError(ErrorValue error)
    {
        var workbook = new Workbook("FormulaCachedError");
        var sheet = workbook.AddSheet("Sheet1");

        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(address, "A2");
        sheet.GetCell(address)!.Value = error;

        return workbook;
    }

    private static Sheet SaveAndReload(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);
        return reloaded.GetSheetAt(0);
    }

    [Theory]
    [InlineData("#SPILL!")]
    [InlineData("#CALC!")]
    public void FormulaCell_SpillAndCalcCachedValue_RoundTripsThroughRealSaveAsItselfNotNA(string code)
    {
        var workbook = CreateWorkbookWithFormulaCachedError(new ErrorValue(code));

        var reloadedSheet = SaveAndReload(workbook);

        // The specific historical J28 bug, applied to the genuinely-exercised t="e" formula-cached
        // path: a real formula's cached #SPILL!/#CALC! must round-trip as itself, never silently
        // become a different, valid-but-wrong error code (#N/A), and must not be lost to BlankValue.
        var reloadedValue = reloadedSheet.GetValue(1, 1);
        reloadedValue.Should().BeOfType<ErrorValue>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void FormulaCell_CircularCachedValue_RoundTripsThroughRealSaveAsZeroNotCircularOrNA()
    {
        var workbook = CreateWorkbookWithFormulaCachedError(ErrorValue.Circular);

        var reloadedSheet = SaveAndReload(workbook);

        // Matches Excel: with iterative calculation off, a circular reference persists as a plain 0,
        // not as a (non-OOXML) "#CIRCULAR!" error cell and not as any other error such as #N/A.
        var reloadedValue = reloadedSheet.GetValue(1, 1);
        reloadedValue.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0d);
    }

    // Classic Excel error codes must still round-trip faithfully through the formula-cached path —
    // this coverage addition must not regress that existing, already-correct behavior.
    [Theory]
    [InlineData("#NULL!")]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#REF!")]
    [InlineData("#NAME?")]
    [InlineData("#NUM!")]
    [InlineData("#N/A")]
    public void FormulaCell_ClassicErrorCodes_StillRoundTripAsThemselves(string code)
    {
        var workbook = CreateWorkbookWithFormulaCachedError(new ErrorValue(code));

        var reloadedSheet = SaveAndReload(workbook);

        var reloadedValue = reloadedSheet.GetValue(1, 1);
        reloadedValue.Should().BeOfType<ErrorValue>().Which.Code.Should().Be(code);
    }
}
