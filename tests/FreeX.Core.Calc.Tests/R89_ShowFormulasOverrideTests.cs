using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R89-show-formulas-per-window-1: <see cref="ViewportRequest.ShowFormulasOverride"/>
/// lets a caller (the WPF host's per-window <c>WorksheetViewStateStore</c>) override the shared
/// <see cref="Sheet.ShowFormulas"/> field for a single viewport build, mirroring the pre-existing
/// <see cref="ViewportRequest.FrozenRowsOverride"/>/<see cref="ViewportRequest.SplitOverride"/> pattern.
/// Leaving it null (the default) must preserve <c>ViewportService.GetDisplayText</c>'s pre-existing
/// behavior of always reading the shared <see cref="Sheet.ShowFormulas"/> field directly -- these
/// tests cover both the override itself and that no-regression default.
/// </summary>
public sealed class R89_ShowFormulasOverrideTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateSheetWithFormulaCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var formulaCell = Cell.FromFormula("A1+1");
        formulaCell.Value = new NumberValue(3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), formulaCell);
        return (workbook, sheet);
    }

    [Fact]
    public void GetViewport_ShowFormulasOverrideTrue_DisplaysFormulaTextEvenWhenSheetShowFormulasIsFalse()
    {
        var (workbook, sheet) = CreateSheetWithFormulaCell();
        sheet.ShowFormulas = false;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300, ShowFormulasOverride: true));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("=A1+1",
                "the override wins over the shared Sheet.ShowFormulas field, exactly like " +
                "FrozenRowsOverride/SplitOverride already do for their own fields");
    }

    [Fact]
    public void GetViewport_ShowFormulasOverrideFalse_DisplaysFormattedValueEvenWhenSheetShowFormulasIsTrue()
    {
        var (workbook, sheet) = CreateSheetWithFormulaCell();
        sheet.ShowFormulas = true;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300, ShowFormulasOverride: false));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("3",
                "an explicit false override must suppress formula display even though the shared " +
                "Sheet still has ShowFormulas on (a sibling window's own state)");
    }

    [Fact]
    public void GetViewport_ShowFormulasOverrideNull_FallsBackToSharedSheetShowFormulasTrue()
    {
        var (workbook, sheet) = CreateSheetWithFormulaCell();
        sheet.ShowFormulas = true;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300, ShowFormulasOverride: null));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("=A1+1",
                "no-regression: leaving the override null must preserve the pre-existing behavior " +
                "of reading the shared Sheet.ShowFormulas field directly");
    }

    [Fact]
    public void GetViewport_ShowFormulasOverrideNull_FallsBackToSharedSheetShowFormulasFalse()
    {
        var (workbook, sheet) = CreateSheetWithFormulaCell();
        sheet.ShowFormulas = false;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300, ShowFormulasOverride: null));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("3",
                "no-regression: leaving the override null must preserve the pre-existing behavior " +
                "of reading the shared Sheet.ShowFormulas field directly");
    }
}
