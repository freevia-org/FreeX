using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// dirty-propagation F1 [MED] / R146 remediation: a Formula-type conditional-format rule built on a
/// volatile function (e.g. "=RAND()&gt;0.5") is cached per (SheetId, Sheet.ContentVersion,
/// ConditionalFormats.Version) on <see cref="ViewportService"/>. A sheet that holds only
/// literal/raw data plus such a rule -- a normal shape, e.g. random row-highlighting applied
/// directly with no helper formula column -- has zero formula cells. Real Excel re-rolls a volatile
/// CF formula on every recalculation pass regardless of whether the workbook contains any other
/// formula cells.
///
/// <para>
/// The r146 fix wave patched this only in <see cref="RecalcEngine.RecalculateAllFormulas"/>, which
/// plain F9 never reaches in the app's default Automatic calculation mode: plain F9 routes through
/// <c>KeyboardShortcutMatcher</c> -&gt; <c>CalculationCommandPolicy.PlanAction</c> (maps
/// CalculateNow to <c>CalculationRecalculationScope.DirtyWorkbook</c>) -&gt;
/// <c>CalculationWorkflowSession</c> -&gt; the host's <c>RecalculateDirtyCells</c> -&gt;
/// <see cref="WorkbookSession.RecalculateDirtyCells"/> -&gt;
/// <see cref="WorkbookCellEditService.RecalculateDirty"/>. In Automatic mode (the app default --
/// see <see cref="Workbook.CalculationMode"/>), <c>RecalculateDirty</c> calls
/// <c>RecalcEngine.Recalculate</c> DIRECTLY -- a different method from
/// <c>RecalculateAllFormulas</c>, which the earlier fix never touched. Only in Manual mode does
/// <c>RecalculateDirty</c> delegate to <see cref="WorkbookCellEditService.RecalculateAll"/> -&gt;
/// <c>RecalculateAllFormulas</c>, the method that WAS patched.
/// </para>
///
/// <para>
/// This test enters through <see cref="WorkbookCellEditService.RecalculateDirty"/> itself (the same
/// method <see cref="WorkbookSession.RecalculateDirtyCells"/> calls for plain F9 in both the WPF and
/// Avalonia shells) in the DEFAULT Automatic calculation mode, so it exercises the real dispatch
/// chain instead of calling <c>RecalculateAllFormulas</c> directly. Fix:
/// <c>RecalculateDirty</c>'s Automatic-mode branch now also calls
/// <see cref="RecalcEngine.NotifyAllSheetsRecalculated"/> after its <c>Recalculate</c> call, the
/// same notification <c>RecalculateAllFormulas</c> performs -- see that method's doc comment for why
/// unconditionally bumping every sheet's ContentVersion on a genuine recalc gesture is safe.
/// </para>
/// </summary>
public sealed class R146_F9RerollsVolatileCfWithNoFormulaCellsTests
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
    public void PlainF9InAutomaticMode_OnSheetWithNoFormulaCells_RerollsVolatileCfRuleAcrossRepeatedPresses()
    {
        var (workbook, sheet, service) = CreateEditService();
        workbook.CalculationMode.Should().Be(
            WorkbookCalculationMode.Automatic,
            "the app's default calc mode, and exactly the mode plain F9 must be proven to work in");

        // Plain literal data only -- zero formula cells anywhere on the sheet.
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var highlight = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "RAND()>0.5", highlight));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        // Seed the first evaluated result via the real plain-F9 entry point.
        service.RecalculateDirty(workbook);
        var firstSet = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);

        // Press F9 (RecalculateDirty) repeatedly with nothing else changed. The sheet has no
        // formula cells, so the recalc report is empty every single time -- but F9 is still a
        // genuine recalc gesture and must let the volatile CF formula re-roll, matching Excel.
        var sawDifferentSet = false;
        for (var i = 0; i < 30 && !sawDifferentSet; i++)
        {
            var report = service.RecalculateDirty(workbook);
            report.RecalculatedCells.Should().BeEmpty("the sheet has no formula cells to recalculate");

            var set = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);
            if (!set.SetEquals(firstSet))
                sawDifferentSet = true;
        }

        sawDifferentSet.Should().BeTrue(
            "plain F9 in Automatic mode (WorkbookCellEditService.RecalculateDirty, the method both " +
            "the WPF and Avalonia shells actually call) must re-roll a volatile Formula-type CF " +
            "rule even when the sheet holds zero formula cells, instead of freezing the cached " +
            "result forever because ContentVersion never advances");
    }

    [Fact]
    public void PlainF9InAutomaticMode_OnSheetWithNoFormulaCells_NonVolatileCfRuleStaysStableAcrossF9()
    {
        // Sibling/no-regression case: a NON-volatile Formula-type CF rule on a sheet with zero
        // formula cells must keep evaluating to the same (correct) result across repeated plain-F9
        // presses -- the fix must not turn every F9 into a random reshuffle of unrelated CF rules,
        // it must simply let ContentVersion advance so the cache is rebuilt (which, for a
        // deterministic formula over unchanged data, naturally reproduces the identical result).
        var (workbook, sheet, service) = CreateEditService();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var highlight = new CellColor(0, 255, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "A1>5", highlight));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        for (var i = 0; i < 5; i++)
        {
            service.RecalculateDirty(workbook);
            var vp = svc.GetViewport(workbook, sheet.Id, request);
            var a1Style = vp.Cells.Single(c => c.Row == 1 && c.Col == 1).Style;
            var a2Style = vp.Cells.Single(c => c.Row == 2 && c.Col == 1).Style;
            a1Style!.FillColor.Should().Be(highlight, "A1=10 > 5, formula true, every F9 press");
            a2Style?.FillColor.Should().NotBe(highlight, "A2=2, shifted formula A2>5 is false, every F9 press");
        }
    }

    /// <summary>
    /// Sibling coverage for the OTHER real F9 path: Manual calculation mode, where
    /// <see cref="WorkbookCellEditService.RecalculateDirty"/> delegates to
    /// <see cref="WorkbookCellEditService.RecalculateAll"/> -&gt;
    /// <see cref="RecalcEngine.RecalculateAllFormulas"/> (the method the original r146 fix touched,
    /// and which Ctrl+Alt+F9's <c>RecalculateAll</c> always uses regardless of calc mode). Kept
    /// alongside the Automatic-mode tests above so both real dispatch branches of
    /// <c>RecalculateDirty</c> stay covered.
    /// </summary>
    [Fact]
    public void PlainF9InManualMode_OnSheetWithNoFormulaCells_RerollsVolatileCfRuleAcrossRepeatedPresses()
    {
        var (workbook, sheet, service) = CreateEditService();
        workbook.CalculationMode = WorkbookCalculationMode.Manual;

        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var highlight = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(AddFormulaRule(sheet, "RAND()>0.5", highlight));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 2000, 500);

        service.RecalculateDirty(workbook);
        var firstSet = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);

        var sawDifferentSet = false;
        for (var i = 0; i < 30 && !sawDifferentSet; i++)
        {
            var report = service.RecalculateDirty(workbook);
            report.RecalculatedCells.Should().BeEmpty("the sheet has no formula cells to recalculate");

            var set = GetHighlightedRows(svc.GetViewport(workbook, sheet.Id, request), highlight);
            if (!set.SetEquals(firstSet))
                sawDifferentSet = true;
        }

        sawDifferentSet.Should().BeTrue(
            "plain F9 in Manual mode (RecalculateDirty -> RecalculateAll -> RecalculateAllFormulas) " +
            "must keep re-rolling a volatile Formula-type CF rule, matching the Automatic-mode path");
    }
}
