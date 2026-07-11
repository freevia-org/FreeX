using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R24-volatile-recalc-deep-2: Shift+F9 "Calculate Sheet" (RecalcEngine.RecalculateSheetFormulas)
/// must only recalculate the requested sheet's formulas -- matching Excel's documented Shift+F9
/// semantics (see WorkbookCellEditService.RecalculateSheet doc comment: "Forces a recalculation of
/// every formula on a single worksheet"). A volatile function (e.g. RAND()) on a DIFFERENT sheet of
/// the same workbook must not be silently re-evaluated (and its ContentVersion bumped) as a side
/// effect of RebuildFormulaDependencies re-registering every sheet's volatile cells into the shared
/// _volatileCells set.
/// </summary>
public sealed class RecalculateSheetFormulasVolatileScopeTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RecalculateSheetFormulas_DoesNotRecalculateVolatileCellsOnOtherSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var engine = Engine();

        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s2b2 = new CellAddress(sheet2.Id, 2, 2);

        // Sheet1 has an ordinary (non-volatile) formula so RecalculateSheetFormulas has something
        // to do on the target sheet.
        sheet1.SetCell(s1a1, new NumberValue(5));

        // Sheet2 has an unrelated volatile formula -- nothing on Sheet1 references it.
        sheet2.SetFormula(s2b2, "RAND()");

        // Seed both sheets' dependency/volatile tracking and get RAND()'s initial value.
        engine.RecalculateAllFormulas(workbook);
        var beforeContentVersion = sheet2.ContentVersion;
        var beforeValue = ((NumberValue)sheet2.GetValue(s2b2)).Value;

        // Shift+F9 on Sheet1 only.
        var report = engine.RecalculateSheetFormulas(workbook, sheet1.Id);

        report.RecalculatedCells.Should().NotContain(s2b2);
        sheet2.ContentVersion.Should().Be(beforeContentVersion,
            "Calculate Sheet on Sheet1 must not touch Sheet2's content version");
        var afterValue = ((NumberValue)sheet2.GetValue(s2b2)).Value;
        afterValue.Should().Be(beforeValue,
            "a volatile cell on a different sheet must not be re-evaluated as a side effect of recalculating Sheet1");
    }

    [Fact]
    public void RecalculateSheetFormulas_StillRecalculatesVolatileCellsOnTargetSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetFormula(a1, "RAND()");

        engine.RecalculateAllFormulas(workbook);
        var beforeValue = ((NumberValue)sheet1.GetValue(a1)).Value;

        var report = engine.RecalculateSheetFormulas(workbook, sheet1.Id);

        report.RecalculatedCells.Should().Contain(a1);
        var afterValue = ((NumberValue)sheet1.GetValue(a1)).Value;
        // Astronomically unlikely to coincide if genuinely re-evaluated.
        afterValue.Should().NotBe(beforeValue);
    }

    [Fact]
    public void RecalculateSheetFormulas_AfterCall_OtherSheetVolatileCellStillTracked_ByLaterFullRecalc()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var engine = Engine();

        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s2b2 = new CellAddress(sheet2.Id, 2, 2);

        sheet1.SetCell(s1a1, new NumberValue(5));
        sheet2.SetFormula(s2b2, "RAND()");

        engine.RecalculateAllFormulas(workbook);
        var afterFirstFull = ((NumberValue)sheet2.GetValue(s2b2)).Value;

        // Scoped recalc must not un-track Sheet2's volatile cell for future full recalcs.
        engine.RecalculateSheetFormulas(workbook, sheet1.Id);

        var report = engine.RecalculateAllFormulas(workbook);
        report.RecalculatedCells.Should().Contain(s2b2);
        var afterSecondFull = ((NumberValue)sheet2.GetValue(s2b2)).Value;
        afterSecondFull.Should().NotBe(afterFirstFull);
    }
}
