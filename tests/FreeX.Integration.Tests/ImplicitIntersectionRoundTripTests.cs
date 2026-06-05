using ClosedXML.Excel;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Integration.Tests;

// Full path for legacy implicit intersection: an xlsx with a plain (non-array) formula that uses a range in
// scalar context loads as Implicit and intersects to a scalar on recalc instead of spilling/#SPILL!.
public class ImplicitIntersectionRoundTripTests
{
    private static MemoryStream BuildXlsxWithPlainFormula(string formula)
    {
        var ms = new MemoryStream();
        using (var xl = new XLWorkbook())
        {
            var ws = xl.AddWorksheet("S");
            for (int c = 1; c <= 10; c++) ws.Cell(7, c).Value = c; // A7:J7 = 1..10
            ws.Cell(15, 2).Value = 2;                              // B15 = 2
            ws.Cell(20, 10).FormulaA1 = formula;                   // plain <f> at J20
            xl.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void PlainFormula_LoadsImplicit_AndIntersectsOnRecalc()
    {
        using var ms = BuildXlsxWithPlainFormula("A7:J7*B15");
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        sheet.GetCell(20, 10)!.ArrayMode.Should().Be(FormulaArrayMode.Implicit);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        // J20 (column 10) implicitly intersects A7:J7 to J7 (=10), * B15 (2) = 20.
        sheet.GetCell(20, 10)!.Value.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void DynamicSpillFormula_SurvivesRebuildSave_AsDynamic()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (uint c = 1; c <= 5; c++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, c), Cell.FromValue(new NumberValue(c))); // A1:E1 = 1..5
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A1:E1*2"));            // Dynamic, spills at A3
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);                 // new workbook -> rebuild (ClosedXML) save path
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        // Must reload as Dynamic (spills), not be mis-detected as a legacy Implicit formula.
        reloaded.GetSheetAt(0).GetCell(3, 1)!.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
    }

    [Fact]
    public void PlainFormula_SurvivesByteCopyRoundTrip_AsImplicit()
    {
        using var ms = BuildXlsxWithPlainFormula("A7:J7*B15");
        var adapter = new XlsxFileAdapter();
        var wb = adapter.Load(ms);

        // Save without edits (byte-copy/patch path) and reload: the mode must still be Implicit.
        using var saved = new MemoryStream();
        adapter.Save(wb, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).GetCell(20, 10)!.ArrayMode.Should().Be(FormulaArrayMode.Implicit);
    }
}
