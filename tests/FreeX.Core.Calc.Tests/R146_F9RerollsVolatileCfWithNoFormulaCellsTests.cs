using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// dirty-propagation F1 [MED]: a Formula-type conditional-format rule built on a volatile
/// function (e.g. "=RAND()&gt;0.5") is cached per (SheetId, Sheet.ContentVersion,
/// ConditionalFormats.Version) on <see cref="ViewportService"/> (see
/// ViewportService.ConditionalFormats.cs / ViewportService.ConditionalFormatFormulas.cs). A sheet
/// that holds only literal/raw data plus such a rule -- a normal shape, e.g. random row-highlighting
/// applied directly with no helper formula column -- has zero formula cells. Before this fix,
/// RecalcEngine.RecalculateAllFormulas (the F9 handler, reached via
/// WorkbookCellEditService.RecalculateAll) only bumped Sheet.ContentVersion for sheets that appeared
/// in the recalc report's RecalculatedCells/CyclicCells/Errors -- so a sheet with no formula cells at
/// all never advanced ContentVersion no matter how many times F9 was pressed, freezing the volatile
/// CF rule's evaluated result at whatever it rolled on the very first render, forever. Real Excel
/// re-rolls a volatile CF formula on every recalculation pass regardless of whether the workbook
/// contains any other formula cells.
///
/// Fix: RecalculateAllFormulas now unconditionally notifies every sheet in the workbook that a
/// genuine recalc pass occurred (Sheet.NotifyContentRecalculated), not just sheets with a non-empty
/// report entry. Sheet.ContentVersion has exactly one consumer (the CF viewport-context cache), so
/// this cannot affect any other cache.
/// </summary>
public sealed class R146_F9RerollsVolatileCfWithNoFormulaCellsTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

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
    public void RecalculateAllFormulas_OnSheetWithNoFormulaCells_RerollsVolatileCfRuleAcrossRepeatedF9Presses()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Plain literal data only -- zero formula cells anywhere on the sheet.
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var highlight = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "RAND()>0.5", highlight));

        var engine = Engine();
        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        // Seed the first evaluated result.
        engine.RecalculateAllFormulas(workbook);
        var firstSet = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);

        // Press F9 repeatedly with nothing else changed. The sheet has no formula cells, so the
        // recalc report is empty every single time -- but F9 is still a genuine recalc gesture and
        // must let the volatile CF formula re-roll, matching Excel.
        var sawDifferentSet = false;
        for (var i = 0; i < 30 && !sawDifferentSet; i++)
        {
            var report = engine.RecalculateAllFormulas(workbook);
            report.RecalculatedCells.Should().BeEmpty("the sheet has no formula cells to recalculate");

            var set = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);
            if (!set.SetEquals(firstSet))
                sawDifferentSet = true;
        }

        sawDifferentSet.Should().BeTrue(
            "F9 must re-roll a volatile Formula-type CF rule even when the sheet holds zero formula " +
            "cells, instead of freezing the cached result forever because ContentVersion never advances");
    }

    [Fact]
    public void RecalculateAllFormulas_OnSheetWithNoFormulaCells_NonVolatileCfRuleStaysStableAcrossF9()
    {
        // Sibling/no-regression case: a NON-volatile Formula-type CF rule on a sheet with zero
        // formula cells must keep evaluating to the same (correct) result across repeated F9
        // presses -- the fix must not turn every F9 into a random reshuffle of unrelated CF rules,
        // it must simply let ContentVersion advance so the cache is rebuilt (which, for a
        // deterministic formula over unchanged data, naturally reproduces the identical result).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var highlight = new CellColor(0, 255, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "A1>5", highlight));

        var engine = Engine();
        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        for (var i = 0; i < 5; i++)
        {
            engine.RecalculateAllFormulas(workbook);
            var vp = svc.GetViewport(workbook, sheet.Id, request);
            var a1Style = vp.Cells.Single(c => c.Row == 1 && c.Col == 1).Style;
            var a2Style = vp.Cells.Single(c => c.Row == 2 && c.Col == 1).Style;
            a1Style!.FillColor.Should().Be(highlight, "A1=10 > 5, formula true, every F9 press");
            a2Style?.FillColor.Should().NotBe(highlight, "A2=2, shifted formula A2>5 is false, every F9 press");
        }
    }
}
