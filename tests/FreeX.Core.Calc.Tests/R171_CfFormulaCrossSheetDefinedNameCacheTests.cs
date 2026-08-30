using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for freex-defined-names F1: a Formula-type conditional-format rule that
/// reads a defined name resolving to a cell on ANOTHER sheet (e.g. "A1&gt;Threshold" where
/// Threshold refers to Sheet2!$B$1) does not refresh when that other sheet's target cell changes.
/// ViewportService.BuildConditionalFormatContext caches the whole per-sheet CF evaluation context
/// (including the per-(rule,cell) formula-result cache) keyed only on
/// (SheetId, sheet.ContentVersion, sheet.ConditionalFormats.Version). A CF rule is not a node in
/// the dependency graph, so editing the OTHER sheet's cell that a defined name points at never
/// bumps THIS sheet's own ContentVersion or ConditionalFormats.Version -- the next GetViewport call
/// hits the same cache key and returns the stale, pre-edit result.
///
/// The fix folds a checksum of every sheet's ContentVersion into the cache key, but only for sheets
/// that actually have a formula-driven CF rule (RuleType.Formula, or a ColorScale/DataBar/IconSet
/// threshold of type Formula) -- see SheetHasFormulaDrivenConditionalFormat in
/// ViewportService.ConditionalFormats.cs.
/// </summary>
public sealed class R171_CfFormulaCrossSheetDefinedNameCacheTests
{
    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void FormulaRule_ReadingDefinedNameOnOtherSheet_RefreshesAfterTargetCellEdited()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var sheet2B1 = new CellAddress(sheet2.Id, 1, 2);
        sheet2.SetCell(sheet2B1, Cell.FromValue(new NumberValue(5)));

        // Workbook-scoped defined name "Threshold" = Sheet2!$B$1.
        wb.DefineNamedRange("Threshold", new GridRange(sheet2B1, sheet2B1));

        // Sheet1!A1 is a plain VALUE (not a formula) -- it never participates in the dependency
        // graph, so RecalcEngine's notification path (already correct since round 13) is
        // irrelevant here; the CF rule itself is the only thing reading across sheets.
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(sheet1A1, Cell.FromValue(new NumberValue(10)));

        var highlight = new CellColor(255, 0, 0);
        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(sheet1A1, sheet1A1),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>Threshold",
            FormatIfTrue = new CellStyle { FillColor = highlight }
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        var vpBefore = svc.GetViewport(wb, sheet1.Id, request);
        GetCell(vpBefore, 1, 1).Style!.FillColor.Should().Be(highlight, "10 > 5 is true");

        // Edit ONLY Sheet2!B1 (the name's target) -- Sheet1 itself is never touched.
        sheet2.SetCell(sheet2B1, Cell.FromValue(new NumberValue(20)));

        var vpAfter = svc.GetViewport(wb, sheet1.Id, request);
        GetCell(vpAfter, 1, 1).Style?.FillColor.Should().NotBe(highlight,
            "10 > 20 is now false -- Excel would clear the highlight immediately, but a stale " +
            "cache keyed only on Sheet1's own (unchanged) ContentVersion would keep serving the " +
            "pre-edit 'true' result");
    }

    [Fact]
    public void NonFormulaRule_OnOtherSheetEdit_DoesNotRebuildCachedContext()
    {
        // Sibling no-regression: a sheet whose CF rules are NOT formula-driven (AboveAverage here)
        // must keep reusing its cached context when an unrelated sheet is edited -- the fix must
        // not pay the cross-sheet invalidation cost for rule types that can never read another
        // sheet's cell via a formula.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), Cell.FromValue(new NumberValue(5)));

        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(wb, sheet1.Id, request);
        var buildCountBefore = svc.CfContextBuildCount;

        // Edit the OTHER sheet -- Sheet1's own ContentVersion/ConditionalFormats.Version are
        // untouched, and Sheet1 has no formula-driven CF rule, so its cached context must survive.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), Cell.FromValue(new NumberValue(999)));

        svc.GetViewport(wb, sheet1.Id, request);
        var buildCountAfter = svc.CfContextBuildCount;

        buildCountAfter.Should().Be(buildCountBefore,
            "AboveAverage never reads another sheet, so editing Sheet2 must not force a rebuild of " +
            "Sheet1's cached CF context");
    }
}
