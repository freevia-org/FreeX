using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// RecalcEngine dependency-tracking tests for 3-D sheet-span references (Finding H28,
/// e.g. SUM(Sheet1:Sheet3!A1)). A formula referencing a span must register a dependency
/// edge on every sheet the span covers — including sheets strictly between the named
/// start/end sheets — so editing any spanned sheet's cell triggers recalculation of the
/// dependent formula.
/// </summary>
public sealed class SheetSpanReferenceRecalcTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RecalcEngine_SheetSpan_SumsAcrossAllSpannedSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:Sheet3!A1)");

        Engine().RecalculateAllFormulas(workbook);

        sheet1.GetValue(1, 2).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void RecalcEngine_EditingMiddleSheet_RecalculatesDependentFormula()
    {
        // The dependent formula lives on Sheet1 and references Sheet1:Sheet3, so Sheet2 (the
        // "middle" sheet, not named directly in the span) must still be a registered dependency.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:Sheet3!A1)");

        var engine = Engine();
        engine.RecalculateAllFormulas(workbook);
        sheet1.GetValue(1, 2).Should().Be(new NumberValue(6));

        // Edit the middle sheet's A1 and recalculate only the changed cell's dependents.
        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(sheet2A1, new NumberValue(200));
        engine.Recalculate(workbook, [sheet2A1]);

        sheet1.GetValue(1, 2).Should().Be(new NumberValue(1 + 200 + 3));
    }

    [Fact]
    public void RecalcEngine_SheetSpan_MissingSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:NoSuchSheet!A1)");

        Engine().RecalculateAllFormulas(workbook);

        sheet1.GetValue(1, 2).Should().Be(ErrorValue.Ref);
    }
}
