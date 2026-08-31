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
    public void CurrentDateSerial_ReturnsExcelNumericSerial()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentDateSerial(now);

        value.Value.Should().Be(DateTimeEntryService.CurrentDate(now).Value);
    }

    [Fact]
    public void CreateCurrentDateShortcutCell_BlankGeneralCellUsesBuiltInShortDateFormat()
    {
        var workbook = new Workbook("Dates");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var cell = DateTimeEntryService.CreateCurrentDateShortcutCell(workbook, address, new DateTime(2026, 5, 14));

        cell.Value.Should().Be(new NumberValue(DateTimeEntryService.CurrentDate(new DateTime(2026, 5, 14)).Value));
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be(DateTimeEntryService.CurrentDateNumberFormat);
    }

    [Fact]
    public void CurrentTime_ReturnsFractionalDayOnly()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentTime(now);

        value.Value.Should().BeApproximately(now.TimeOfDay.TotalDays, 0.0000000001);
    }
}
