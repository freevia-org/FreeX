using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookValidationCircleWorkflowTests
{
    [Fact]
    public void CircleInvalidData_OwnsPerSheetStateAndReturnsFirstInvalidCell()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var firstAddress = AddWholeNumberValidation(first, 1, 1, value: 50);
        var secondAddress = AddWholeNumberValidation(second, 2, 2, value: 50);
        second.ValidationCircleCells = [secondAddress];

        var result = WorkbookValidationCircleWorkflow.CircleInvalidData(workbook, first);

        result.Outcome.Should().Be(WorkbookValidationCircleOutcome.Circled);
        result.Cells.Should().Equal(firstAddress);
        result.FirstCell.Should().Be(firstAddress);
        first.ValidationCircleCells.Should().Equal(firstAddress);
        second.ValidationCircleCells.Should().ContainSingle().Which.Should().Be(
            secondAddress,
            "circling one sheet must not replace another sheet's transient overlay state");
    }

    [Fact]
    public void CircleInvalidData_NoInvalidDataClearsStaleState()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var address = AddWholeNumberValidation(sheet, 1, 1, value: 5);
        sheet.ValidationCircleCells = [address];

        var result = WorkbookValidationCircleWorkflow.CircleInvalidData(workbook, sheet);

        result.Outcome.Should().Be(WorkbookValidationCircleOutcome.NoInvalidData);
        result.HasCircles.Should().BeFalse();
        result.FirstCell.Should().BeNull();
        sheet.ValidationCircleCells.Should().BeNull();
    }

    [Fact]
    public void Clear_ReportsWhetherAnythingWasRemoved()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ValidationCircleCells = [address];

        var cleared = WorkbookValidationCircleWorkflow.Clear(sheet);
        var empty = WorkbookValidationCircleWorkflow.Clear(sheet);

        cleared.Outcome.Should().Be(WorkbookValidationCircleOutcome.Cleared);
        cleared.RemovedCount.Should().Be(1);
        empty.Outcome.Should().Be(WorkbookValidationCircleOutcome.NothingToClear);
        sheet.ValidationCircleCells.Should().BeNull();
    }

    [Fact]
    public void Prune_RemovesCorrectedAndForeignAddressesFromSheetOwnedState()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var other = workbook.AddSheet("Sheet2");
        var corrected = AddWholeNumberValidation(sheet, 1, 1, value: 5);
        var stillInvalid = AddWholeNumberValidation(sheet, 2, 1, value: 50);
        var foreign = new CellAddress(other.Id, 1, 1);
        sheet.ValidationCircleCells = [corrected, stillInvalid, foreign];

        var result = WorkbookValidationCircleWorkflow.Prune(workbook, sheet);

        result.Outcome.Should().Be(WorkbookValidationCircleOutcome.Pruned);
        result.Cells.Should().Equal(stillInvalid);
        result.RemovedCount.Should().Be(2);
        sheet.ValidationCircleCells.Should().Equal(stillInvalid);
    }

    private static CellAddress AddWholeNumberValidation(
        Sheet sheet,
        uint row,
        uint column,
        double value)
    {
        var address = new CellAddress(sheet.Id, row, column);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AlertStyle = DvAlertStyle.Warning,
            ShowErrorMessage = true,
        });
        sheet.SetCell(address, new NumberValue(value));
        return address;
    }
}
