using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R18-calc-chain-fullcalc-2: the recalc-on-edit gate must treat
/// <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/> like
/// <see cref="WorkbookCalculationMode.Automatic"/> for ordinary cell-edit recalculation. Only
/// What-If Analysis Data Table recalcs are deferred under "Automatic Except Data Tables" -- a plain
/// dependent formula like B1=A1+1 must still recalc when A1 changes.
/// </summary>
public sealed class R18_calc_mode_Tests
{
    [Fact]
    public void CommitCellText_RecalculatesDependents_WhenCalculationModeIsAutomaticExceptDataTables()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        result.RecalcReport.Should().NotBeNull();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void RecalculateIfAutomatic_Recalculates_WhenCalculationModeIsAutomaticExceptDataTables()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;
        sheet.SetCell(a1, new NumberValue(10));

        var report = service.RecalculateIfAutomatic(workbook, [a1]);

        report.Should().NotBeNull();
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(11);
    }

    private static (
        Workbook Workbook,
        Sheet Sheet,
        CommandBus CommandBus,
        WorkbookCellEditService Service,
        RecalcEngine RecalcEngine) CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, commandBus, service, recalcEngine);
    }
}
