using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookCellEditServiceTests
{
    [Fact]
    public void CommitCellText_UsesCommandBusAndRecalculatesDependents()
    {
        var (workbook, sheet, commandBus, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a1);
        commandBus.CanUndo(workbook.Id).Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void CommitCellText_ConvertsFormulaAndRecalculatesEditedFormula()
    {
        var (workbook, sheet, _, service, _) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(3));

        var result = service.CommitCellText(workbook, sheet.Id, b2, "=R[-1]C[-1]*2", useR1C1ReferenceStyle: true);

        result.Success.Should().BeTrue();
        sheet.GetCell(b2)!.FormulaText.Should().Be("A1*2");
        sheet.GetCell(b2)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(6);
    }

    [Fact]
    public void CommitCellText_LeavesDependentsStaleWhenCalculationModeIsManual()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        result.RecalcReport.Should().BeNull();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2);
    }

    [Fact]
    public void CommitCellText_ReturnsCommandFailureForProtectedSheet()
    {
        var (workbook, sheet, commandBus, service, _) = CreateEditService();
        sheet.IsProtected = true;
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var result = service.CommitCellText(workbook, sheet.Id, a1, "blocked");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("The sheet is protected.");
        commandBus.CanUndo(workbook.Id).Should().BeFalse();
        sheet.GetCell(a1).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_AllowsLockedCellInsideAllowedEditRangeOnProtectedSheet()
    {
        var (workbook, sheet, commandBus, service, _) = CreateEditService();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.AllowEditRanges.Add(new GridRange(b2, b2));
        sheet.IsProtected = true;

        var result = service.CommitCellText(workbook, sheet.Id, b2, "allowed");

        result.Success.Should().BeTrue();
        commandBus.CanUndo(workbook.Id).Should().BeTrue();
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("allowed"));
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
