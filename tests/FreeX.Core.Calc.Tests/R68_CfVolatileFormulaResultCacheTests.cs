using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R68-calc-volatile-async-6-1: a Formula-type conditional-format rule
/// built on a volatile function (e.g. "=RAND()&gt;0.5") was re-evaluated on every
/// <see cref="ViewportService.GetViewport"/> call -- including pure render passes (scroll/resize)
/// that touch no cell content -- because ViewportService.MatchesFormula unconditionally
/// re-invoked the formula evaluator every time it ran, even though the CfEvaluationContext it read
/// from (cfContext.Formulas) was already cached and reused across those calls. That made the set
/// of highlighted cells randomize on every render instead of only on a genuine recalc, unlike
/// Excel which only re-evaluates volatile CF formulas when the workbook actually recalculates.
///
/// The fix adds a per-(rule, cell) evaluated-result cache (CfEvaluationContext.FormulaResults)
/// that lives exactly as long as the CfEvaluationContext itself -- which is rebuilt only when
/// Sheet.ContentVersion or the conditional-format rule set changes (a genuine edit/recalc), not on
/// a render-only viewport request.
/// </summary>
public sealed class R68_CfVolatileFormulaResultCacheTests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook() =>
        TestWorkbookFixture.CreateWorkbook();

    private static ViewportModel GetViewport(ViewportService svc, Workbook wb, Sheet sheet)
    {
        // Tall enough viewport to cover A1:A20 in one request.
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 2000, 500));
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

    private static ConditionalFormat AddFormulaRule(Sheet sheet, string formulaText, CellColor color, int priority = 1)
    {
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 1)),
            Priority = priority,
            RuleType = CfRuleType.Formula,
            FormulaText = formulaText,
            FormatIfTrue = new CellStyle { FillColor = color }
        };
        sheet.ConditionalFormats.Add(cf);
        return cf;
    }

    [Fact]
    public void VolatileFormulaRule_HighlightSetStaysStableAcrossRepeatedViewportCallsWithNoContentChange()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var highlight = new CellColor(255, 0, 0);
        AddFormulaRule(sheet, "RAND()>0.5", highlight);

        var svc = new ViewportService();
        var firstSet = GetHighlightedRows(GetViewport(svc, wb, sheet), highlight);

        // Repeated render-only requests (e.g. scroll/resize) with no intervening edit must not
        // re-randomize which cells are highlighted: Excel only re-evaluates a volatile CF formula
        // on a genuine recalc, not on every render.
        for (var i = 0; i < 5; i++)
        {
            var set = GetHighlightedRows(GetViewport(svc, wb, sheet), highlight);
            set.Should().BeEquivalentTo(firstSet,
                "a volatile CF formula's evaluated result must be cached per render generation " +
                "(invalidated only on a real recalc/content change), not re-rolled on every viewport render");
        }
    }

    [Fact]
    public void VolatileFormulaRule_HighlightSetRefreshesAfterAGenuineContentChange()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var highlight = new CellColor(255, 0, 0);
        AddFormulaRule(sheet, "RAND()>0.5", highlight);

        var svc = new ViewportService();
        var firstSet = GetHighlightedRows(GetViewport(svc, wb, sheet), highlight);

        // A genuine edit bumps Sheet.ContentVersion, which must invalidate the cached CF formula
        // context (and with it the per-cell result cache) so the volatile formula re-rolls. Touch
        // an unrelated cell (outside the CF range) on each round so only ContentVersion changes.
        var sawDifferentSet = false;
        for (uint round = 1; round <= 12 && !sawDifferentSet; round++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 5), Cell.FromValue(new NumberValue(round)));
            var set = GetHighlightedRows(GetViewport(svc, wb, sheet), highlight);
            if (!set.SetEquals(firstSet))
                sawDifferentSet = true;
        }

        sawDifferentSet.Should().BeTrue(
            "after a genuine recalc/content change the volatile CF formula must be free to " +
            "re-evaluate to a different result, not stay frozen at the first-ever evaluated value");
    }

    [Fact]
    public void NonVolatileFormulaRule_UnaffectedByResultCaching()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));

        var highlight = new CellColor(0, 255, 0);
        AddFormulaRule(sheet, "A1>5", highlight);

        var svc = new ViewportService();

        // Stable across repeated render-only calls, same as before this change.
        for (var i = 0; i < 3; i++)
        {
            var vp = GetViewport(svc, wb, sheet);
            var a1Style = vp.Cells.Single(c => c.Row == 1 && c.Col == 1).Style;
            var a2Style = vp.Cells.Single(c => c.Row == 2 && c.Col == 1).Style;
            a1Style!.FillColor.Should().Be(highlight, "A1=10 > 5, formula true, every render");
            a2Style?.FillColor.Should().NotBe(highlight, "A2=2, shifted formula A2>5 is false, every render");
        }

        // And it correctly tracks a genuine content change to the referenced cell.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(9)));
        var vpAfterEdit = GetViewport(svc, wb, sheet);
        vpAfterEdit.Cells.Single(c => c.Row == 2 && c.Col == 1).Style!.FillColor.Should().Be(
            highlight, "after the edit A2=9 > 5, the formula must re-evaluate to true, not stay cached as false");
    }
}
