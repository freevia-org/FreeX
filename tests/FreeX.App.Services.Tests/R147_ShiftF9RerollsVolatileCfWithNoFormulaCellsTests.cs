using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// meta F2 [MED] / R147: Shift+F9 "Calculate Sheet" (<see cref="WorkbookCellEditService.RecalculateSheet"/>
/// -&gt; <see cref="RecalcEngine.RecalculateSheetFormulas"/>, wired to
/// <c>WorkbookSession.RecalculateActiveSheet</c>) must re-roll a volatile Formula-type
/// conditional-format rule even on a sheet holding zero formula cells of its own -- the same bug the
/// r146 fix wave patched for plain F9 (<see cref="WorkbookCellEditService.RecalculateDirty"/>) and
/// Ctrl+Alt+F9 / Manual-mode F9 (<see cref="RecalcEngine.RecalculateAllFormulas"/>), but missed on
/// this third "Calculate Now"-shaped gesture. <see cref="RecalcEngine.RecalculateSheetFormulas"/>
/// only bumps a sheet's <see cref="Sheet.ContentVersion"/> via <c>Recalculate</c>'s own
/// RecalculatedCells/CyclicCells/Errors-gated notification, so a sheet with no formula cells never
/// advances ContentVersion no matter how many times Shift+F9 runs, freezing the CF viewport-context
/// cache (keyed on ContentVersion) at whatever it evaluated to on the first press.
/// </summary>
public sealed class R147_ShiftF9RerollsVolatileCfWithNoFormulaCellsTests
{
    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service) CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, service);
    }

    private static HashSet<uint> GetHighlightedRows(ViewportModel vp, CellColor highlightColor)
    {
        var rows = new HashSet<uint>();
        foreach (var cell in vp.Cells)
        {
            if (cell.Col == 1 && cell.Style?.FillColor == highlightColor)
                rows.Add(cell.Row);
        }
        return rows;
    }

    private static ConditionalFormat AddFormulaRule(Sheet sheet, string formulaText, CellColor color) =>
        new()
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = formulaText,
            FormatIfTrue = new CellStyle { FillColor = color }
        };

    [Fact]
    public void ShiftF9_OnSheetWithNoFormulaCells_RerollsVolatileCfRuleAcrossRepeatedPresses()
    {
        var (workbook, sheet, service) = CreateEditService();

        // Plain literal data only -- zero formula cells anywhere on the sheet.
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var highlight = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "RAND()>0.5", highlight));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        // Seed the first evaluated result via the real Shift+F9 entry point.
        service.RecalculateSheet(workbook, sheet.Id);
        var firstSet = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);

        // Press Shift+F9 (RecalculateSheet) repeatedly with nothing else changed. The sheet has no
        // formula cells, so the recalc report is empty every single time -- but Shift+F9 is still a
        // genuine recalc gesture and must let the volatile CF formula re-roll, matching Excel and
        // matching plain F9 / Ctrl+Alt+F9 on the same sheet.
        var sawDifferentSet = false;
        for (var i = 0; i < 30 && !sawDifferentSet; i++)
        {
            var report = service.RecalculateSheet(workbook, sheet.Id);
            report.RecalculatedCells.Should().BeEmpty("the sheet has no formula cells to recalculate");

            var set = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);
            if (!set.SetEquals(firstSet))
                sawDifferentSet = true;
        }

        sawDifferentSet.Should().BeTrue(
            "Shift+F9 Calculate Sheet (WorkbookCellEditService.RecalculateSheet, the method both " +
            "the WPF and Avalonia shells actually call via WorkbookSession.RecalculateActiveSheet) " +
            "must re-roll a volatile Formula-type CF rule even when the sheet holds zero formula " +
            "cells, instead of freezing the cached result forever because ContentVersion never " +
            "advances");
    }

    /// <summary>
    /// Sibling/no-regression case: Shift+F9 on Sheet1 must still leave a DIFFERENT sheet's own
    /// ContentVersion (and its volatile cell values) completely untouched -- Excel's Shift+F9 only
    /// recalculates the target sheet. This is the same contract
    /// RecalculateSheetFormulasVolatileScopeTests.RecalculateSheetFormulas_DoesNotRecalculateVolatileCellsOnOtherSheets
    /// pins at the RecalcEngine layer; the fix must notify only the target sheet, not every sheet in
    /// the workbook the way NotifyAllSheetsRecalculated does for F9/Ctrl+Alt+F9.
    /// </summary>
    [Fact]
    public void ShiftF9_OnOneSheet_DoesNotBumpAnotherSheetsContentVersionOrRerollItsVolatileCf()
    {
        var (workbook, sheet1, service) = CreateEditService();
        var sheet2 = workbook.AddSheet("Sheet2");

        for (uint row = 1; row <= 20; row++)
            sheet1.SetCell(new CellAddress(sheet1.Id, row, 1), new NumberValue(row));

        for (uint row = 1; row <= 20; row++)
            sheet2.SetCell(new CellAddress(sheet2.Id, row, 1), new NumberValue(row));

        var highlight = new CellColor(255, 0, 0);
        sheet2.ConditionalFormats.Add(AddFormulaRule(sheet2, "RAND()>0.5", highlight));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        // Seed Sheet2's cache once via a real recalc gesture, then only ever Shift+F9 Sheet1.
        service.RecalculateSheet(workbook, sheet1.Id);
        var sheet2FirstSet = GetHighlightedRows(svc.GetViewport(workbook, sheet2.Id, request), highlight);
        var sheet2VersionAfterSeed = sheet2.ContentVersion;

        for (var i = 0; i < 30; i++)
            service.RecalculateSheet(workbook, sheet1.Id);

        sheet2.ContentVersion.Should().Be(sheet2VersionAfterSeed,
            "Shift+F9 on Sheet1 must not bump Sheet2's ContentVersion");
        var sheet2SetAfter = GetHighlightedRows(svc.GetViewport(workbook, sheet2.Id, request), highlight);
        sheet2SetAfter.Should().BeEquivalentTo(sheet2FirstSet,
            "Sheet2's volatile CF cache must stay frozen -- only Sheet1 was Calculate-Sheet'd");
    }
}
