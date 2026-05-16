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
    public void RemoveWatches_RemovesEveryWatchedCellInSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 1, 2);
        var third = new CellAddress(sheet.Id, 2, 1);
        var fourth = new CellAddress(sheet.Id, 2, 2);
        var outside = new CellAddress(sheet.Id, 3, 1);
        foreach (var address in new[] { first, second, third, outside })
            WatchWindowService.AddWatch(workbook, address);

        var removed = WatchWindowService.RemoveWatches(workbook, new GridRange(first, fourth));

        removed.Should().Be(3);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(outside);
    }

    [Fact]
    public void RemoveWatches_SkipsUnwatchedCellsInSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var watched = new CellAddress(sheet.Id, 1, 2);
        WatchWindowService.AddWatch(workbook, watched);

        var removed = WatchWindowService.RemoveWatches(
            workbook,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)));

        removed.Should().Be(1);
        workbook.WatchedCells.Should().BeEmpty();
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

    [Fact]
    public void GetEntries_ReturnsWatchesInWorkbookSheetAndCellOrder()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet1B2 = new CellAddress(sheet1.Id, 2, 2);
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);

        WatchWindowService.AddWatch(workbook, sheet2A1);
        WatchWindowService.AddWatch(workbook, sheet1B2);
        WatchWindowService.AddWatch(workbook, sheet1A1);

        WatchWindowService.GetEntries(workbook).Select(entry => entry.Address)
            .Should().Equal(sheet1A1, sheet1B2, sheet2A1);
    }

    [Fact]
    public void GetDeleteTargets_ReturnsDistinctSelectedAddressesInSelectionOrder()
    {
        var sheet = SheetId.New();
        var first = new CellAddress(sheet, 1, 1);
        var second = new CellAddress(sheet, 2, 1);
        var fallback = new CellAddress(sheet, 3, 1);

        WatchWindowService.GetDeleteTargets([first, second, first], fallback)
            .Should().Equal(first, second);
    }

    [Fact]
    public void GetDeleteTargets_UsesFallbackWhenSelectionIsEmpty()
    {
        var sheet = SheetId.New();
        var fallback = new CellAddress(sheet, 3, 1);

        WatchWindowService.GetDeleteTargets([], fallback)
            .Should().ContainSingle().Which.Should().Be(fallback);
    }
}
