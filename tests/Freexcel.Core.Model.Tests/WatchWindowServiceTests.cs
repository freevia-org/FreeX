using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class WatchWindowServiceTests
{
    [Fact]
    public void AddWatch_AddsCellOnceAndGetEntriesReportsCurrentValueAndFormula()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell { FormulaText = "B1*2", Value = new NumberValue(10) });

        WatchWindowService.AddWatch(workbook, address).Should().BeTrue();
        WatchWindowService.AddWatch(workbook, address).Should().BeFalse();

        var entry = WatchWindowService.GetEntries(workbook).Should().ContainSingle().Subject;
        entry.SheetName.Should().Be("Sheet1");
        entry.Address.Should().Be(address);
        entry.FormulaText.Should().Be("=B1*2");
        entry.ValueText.Should().Be("10");
    }

    [Fact]
    public void RemoveWatch_RemovesWatchedCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        WatchWindowService.AddWatch(workbook, address);

        WatchWindowService.RemoveWatch(workbook, address).Should().BeTrue();

        WatchWindowService.GetEntries(workbook).Should().BeEmpty();
    }

    [Fact]
    public void AddWatches_AddsEveryCellInSelectionAndSkipsExistingWatches()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 1, 2);
        var third = new CellAddress(sheet.Id, 2, 1);
        var fourth = new CellAddress(sheet.Id, 2, 2);
        WatchWindowService.AddWatch(workbook, second);

        var added = WatchWindowService.AddWatches(workbook, new GridRange(first, fourth));

        added.Should().Be(3);
        workbook.WatchedCells.Should().Equal(second, first, third, fourth);
    }
}
