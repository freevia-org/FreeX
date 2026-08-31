using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DateTimeEntryServiceTests
{
    [Fact]
    public void CurrentDate_ReturnsDateWithMidnightTime()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentDate(now);

        value.ToDateTime().Should().Be(new DateTime(2026, 5, 14));
    }

    [Fact]
    public void CreateCurrentDateShortcutCell_BlankGeneralCellStoresNumericSerialWithBuiltInShortDateFormat()
    {
        var workbook = new Workbook("Dates");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var cell = DateTimeEntryService.CreateCurrentDateShortcutCell(workbook, address, new DateTime(2026, 8, 31));

        cell.Value.Should().Be(new NumberValue(DateTimeEntryService.CurrentDate(new DateTime(2026, 8, 31)).Value));
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be(DateTimeEntryService.CurrentDateNumberFormat);
    }

    [Fact]
    public void CreateCurrentDateShortcutCell_ExistingNonGeneralFormatIsPreserved()
    {
        var workbook = new Workbook("Dates");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" });
        sheet.SetCell(address, new Cell { Value = BlankValue.Instance, StyleId = styleId });

        var cell = DateTimeEntryService.CreateCurrentDateShortcutCell(workbook, address, new DateTime(2026, 8, 31));

        cell.StyleId.Should().Be(styleId);
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("yyyy-mm-dd");
    }

    [Fact]
    public void CurrentTime_ReturnsFractionalDayOnly()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentTime(now);

        value.Value.Should().BeApproximately(now.TimeOfDay.TotalDays, 0.0000000001);
    }
}
