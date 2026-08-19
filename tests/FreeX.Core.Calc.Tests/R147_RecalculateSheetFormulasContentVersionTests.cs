using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// meta F2 [MED] / R147: <see cref="RecalcEngine.RecalculateSheetFormulas"/> (Shift+F9 "Calculate
/// Sheet") must advance the target sheet's <see cref="Sheet.ContentVersion"/> on every call, even
/// when the sheet has zero formula cells and <c>Recalculate</c>'s own report is therefore empty.
/// <see cref="Sheet.ContentVersion"/> is the sole cache key for
/// <c>ViewportService.BuildConditionalFormatContext</c>'s volatile-CF-result cache, so without this a
/// volatile Formula-type conditional-format rule (e.g. "=RAND()&gt;0.5") on a sheet holding only
/// literal data never re-rolls across repeated Shift+F9 presses -- the same bug class the r146 fix
/// wave patched for <see cref="RecalcEngine.RecalculateAllFormulas"/> (F9 Manual / Ctrl+Alt+F9) via
/// <see cref="RecalcEngine.NotifyAllSheetsRecalculated"/>, but missed on this third entry point.
/// </summary>
public sealed class R147_RecalculateSheetFormulasContentVersionTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RecalculateSheetFormulas_OnSheetWithNoFormulaCells_StillAdvancesContentVersion()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        // Plain literal data only -- zero formula cells anywhere on the sheet, matching the exact
        // "volatile Formula-type CF rule with no helper formula column" shape from the finding.
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var report = engine.RecalculateSheetFormulas(workbook, sheet.Id);
        report.RecalculatedCells.Should().BeEmpty("the sheet has no formula cells to recalculate");

        var versionAfterFirstCall = sheet.ContentVersion;

        for (var i = 0; i < 5; i++)
        {
            var beforeVersion = sheet.ContentVersion;
            engine.RecalculateSheetFormulas(workbook, sheet.Id);
            sheet.ContentVersion.Should().BeGreaterThan(beforeVersion,
                "Shift+F9 'Calculate Sheet' is a genuine recalc gesture and must let the sheet's " +
                "cached volatile CF results re-roll every time it runs, even with an empty recalc " +
                "report -- otherwise ViewportService's ContentVersion-keyed CF cache freezes forever");
        }

        sheet.ContentVersion.Should().BeGreaterThan(versionAfterFirstCall);
    }

    /// <summary>
    /// Sibling/no-regression case: a Shift+F9 on Sheet1 must still leave Sheet2's ContentVersion
    /// completely untouched, matching
    /// <see cref="RecalculateSheetFormulasVolatileScopeTests.RecalculateSheetFormulas_DoesNotRecalculateVolatileCellsOnOtherSheets"/>.
    /// The fix must notify only the target sheet, never every sheet in the workbook the way
    /// <see cref="RecalcEngine.NotifyAllSheetsRecalculated"/> does for F9/Ctrl+Alt+F9.
    /// </summary>
    [Fact]
    public void RecalculateSheetFormulas_OnOneSheet_DoesNotAdvanceAnotherSheetsContentVersion()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var engine = Engine();

        for (uint row = 1; row <= 20; row++)
        {
            sheet1.SetCell(new CellAddress(sheet1.Id, row, 1), new NumberValue(row));
            sheet2.SetCell(new CellAddress(sheet2.Id, row, 1), new NumberValue(row));
        }

        engine.RecalculateSheetFormulas(workbook, sheet1.Id);
        var sheet2VersionBefore = sheet2.ContentVersion;

        for (var i = 0; i < 5; i++)
            engine.RecalculateSheetFormulas(workbook, sheet1.Id);

        sheet2.ContentVersion.Should().Be(sheet2VersionBefore,
            "Shift+F9 on Sheet1 must not bump Sheet2's ContentVersion -- Calculate Sheet is scoped " +
            "to the target sheet only");
    }
}
