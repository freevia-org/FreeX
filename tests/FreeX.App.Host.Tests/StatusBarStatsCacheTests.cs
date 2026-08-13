using System.IO;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarStatsCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesStatsWhenSheetRangeAndRevisionAreUnchanged()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var calls = 0;

        cache.GetOrCreate(sheet, range, revision: 4, CreateStats);
        var second = cache.GetOrCreate(sheet, range, revision: 4, CreateStats);

        calls.Should().Be(1);
        second.Should().Be(new WorkbookSelectionStats(42, 2, 2, 21, 10, 32));
        return;

        WorkbookSelectionStats CreateStats()
        {
            calls++;
            return new WorkbookSelectionStats(42, 2, 2, 21, 10, 32);
        }
    }

    [Fact]
    public void GetOrCalculate_ReusesStatsWhenSheetRangeAndRevisionAreUnchanged()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(7)));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var first = cache.GetOrCalculate(sheet, range, revision: 4);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(8)));
        var second = cache.GetOrCalculate(sheet, range, revision: 4);
        var third = cache.GetOrCalculate(sheet, range, revision: 5);

        first.Should().Be(new WorkbookSelectionStats(7, 1, 1, 7, 7, 7));
        second.Should().Be(first);
        third.Should().Be(new WorkbookSelectionStats(8, 1, 1, 8, 8, 8));
    }

    [Fact]
    public void GetOrCreate_RecalculatesWhenRevisionChanges()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var calls = 0;

        cache.GetOrCreate(sheet, range, revision: 4, CreateStats);
        var second = cache.GetOrCreate(sheet, range, revision: 5, CreateStats);

        calls.Should().Be(2);
        second.Sum.Should().Be(2);
        return;

        WorkbookSelectionStats CreateStats()
        {
            calls++;
            return new WorkbookSelectionStats(calls, calls, calls, calls, calls, calls);
        }
    }

    [Fact]
    public void Clear_DropsCachedStats()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var calls = 0;

        cache.GetOrCreate(sheet, range, revision: 4, CreateStats);
        cache.Clear();
        cache.GetOrCreate(sheet, range, revision: 4, CreateStats);

        calls.Should().Be(2);
        return;

        WorkbookSelectionStats CreateStats()
        {
            calls++;
            return new WorkbookSelectionStats(calls, calls, calls, calls, calls, calls);
        }
    }

    [Fact]
    public void GetOrCreate_PreservesAggregateErrorCodeThroughSharedConversionRoundTrip()
    {
        // The Host cache now owns the shared stats record directly; aggregate errors must survive caching.
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var stats = cache.GetOrCreate(
            sheet,
            range,
            revision: 4,
            () => new WorkbookSelectionStats(30, 3, 2, 15, 10, 20, "#DIV/0!"));

        stats.AggregateErrorCode.Should().Be("#DIV/0!");
    }

    [Fact]
    public void HostUsesSharedWorkbookSelectionStatsCacheDirectly()
    {
        var hostCachePath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "StatusBarStatsCache.cs");
        var mainSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var calculatorPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "StatusBarCalculator.cs");

        File.Exists(hostCachePath).Should().BeFalse();
        mainSource.Should().Contain("private readonly WorkbookSelectionStatsCache _statusBarStatsCache = new();");
        File.Exists(calculatorPath).Should().BeFalse();
    }
}
