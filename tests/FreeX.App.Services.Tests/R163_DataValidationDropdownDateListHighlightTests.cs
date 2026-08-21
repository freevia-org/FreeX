using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R163-DV-F1: the in-cell List-validation dropdown for a date-sourced range never highlighted
/// the active cell's own (valid) current value. <see cref="DataValidationDropdownPlanner.TryPlan"/>
/// computes the active cell's current display text via <c>SpreadsheetDisplayFormatter.FormatCellValue</c>
/// (a formatted date, e.g. "2024-01-02"), then looks for that exact string among
/// <c>DataValidationService.GetListItems</c>'s items to compute <c>SelectedItem</c>. Before the
/// fix, those items were raw OADate serials ("45293"), which could never match the formatted
/// current-cell text, so <c>SelectedItem</c> was always null for a date-sourced list. Fixing
/// <c>GetListItems</c> to render dates the same way the grid does closes that gap without any
/// change to this planner.
/// </summary>
public sealed class R163_DataValidationDropdownDateListHighlightTests
{
    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");
        return workbook;
    }

    [Fact]
    public void TryPlan_DateSourcedList_ShowsFormattedDatesAndHighlightsCurrentValue()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45293)); // A1 = 2024-01-02
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(45294)); // A2 = 2024-01-03

        var target = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetCell(target, new DateTimeValue(45293)); // B1's own value is A1's date -- a valid list member

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$2",
            ShowDropdown = true,
        });

        var planned = DataValidationDropdownPlanner.TryPlan(
            workbook,
            sheet,
            target,
            new DataValidationDropdownCellBounds(20, 30, 240, 12),
            out var plan);

        planned.Should().BeTrue();
        plan.Items.Should().Equal(
            new[] { "2024-01-02", "2024-01-03" },
            "the dropdown must list the dates the user authored, not raw OADate serials");
        plan.SelectedItem.Should().Be(
            "2024-01-02",
            "the active cell's own current value (2024-01-02) is one of the listed dates and " +
            "must be highlighted, the way real Excel's in-cell dropdown does");
    }
}
