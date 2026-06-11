using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class DataValidationDropdownPlannerTests
{
    [Fact]
    public void TryPlan_ReturnsItemsSelectionAndBoundedCellChrome()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(target, new TextValue("Closed"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Open,Closed,Blocked",
            ShowDropdown = true
        });

        var planned = DataValidationDropdownPlanner.TryPlan(
            workbook,
            sheet,
            target,
            new DataValidationDropdownCellBounds(20, 30, 240, 12),
            out var plan);

        planned.Should().BeTrue();
        plan.Items.Should().Equal("Open", "Closed", "Blocked");
        plan.SelectedItem.Should().Be("Closed");
        plan.Bounds.Should().Be(new DataValidationDropdownBounds(100, 30, 160, 18));
    }

    [Fact]
    public void TryPlan_UsesCellWidthWhenItIsNarrowerThanMaximum()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Yes,No",
            ShowDropdown = true
        });

        DataValidationDropdownPlanner.TryPlan(
                workbook,
                sheet,
                target,
                new DataValidationDropdownCellBounds(10, 10, 72, 20),
                out var plan)
            .Should()
            .BeTrue();

        plan.Bounds.Should().Be(new DataValidationDropdownBounds(10, 10, 72, 20));
    }

    [Theory]
    [InlineData(DvType.WholeNumber, true, "1,2")]
    [InlineData(DvType.List, false, "1,2")]
    [InlineData(DvType.List, true, "")]
    public void TryPlan_RejectsCellsWithoutUsableDropdownRule(DvType type, bool showDropdown, string? formula)
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = type,
            Formula1 = formula,
            ShowDropdown = showDropdown
        });

        DataValidationDropdownPlanner.TryPlan(
                workbook,
                sheet,
                target,
                new DataValidationDropdownCellBounds(0, 0, 64, 20),
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryPlan_RejectsAddressesFromAnotherSheet()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        DataValidationDropdownPlanner.TryPlan(
                workbook,
                sheet,
                new CellAddress(SheetId.New(), 1, 1),
                new DataValidationDropdownCellBounds(0, 0, 64, 20),
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryPlan_RejectsOversizedDropdownSourceWithoutEnumeratingIt()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 1, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$10001",
            ShowDropdown = true
        });

        DataValidationDropdownPlanner.TryPlan(
                workbook,
                sheet,
                target,
                new DataValidationDropdownCellBounds(0, 0, 64, 20),
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryPlan_RejectsHugeDropdownSourceWithoutThrowing()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 1, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "=$A$1:$XFD$1048576",
            ShowDropdown = true
        });

        var act = () => DataValidationDropdownPlanner.TryPlan(
            workbook,
            sheet,
            target,
            new DataValidationDropdownCellBounds(0, 0, 64, 20),
            out _);

        act.Should().NotThrow().Which.Should().BeFalse();
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
