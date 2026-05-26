using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class StatusBarStatsCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesStatsWhenSheetRangeAndRevisionAreUnchanged()
    {
        var cache = new StatusBarStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var calls = 0;

        cache.GetOrCreate(sheet, range, revision: 4, CreateStats);
        var second = cache.GetOrCreate(sheet, range, revision: 4, CreateStats);

        calls.Should().Be(1);
        second.Should().Be(new StatusBarCalculator.Stats(42, 2, 21, 10, 32));
        return;

        StatusBarCalculator.Stats CreateStats()
        {
            calls++;
            return new StatusBarCalculator.Stats(42, 2, 21, 10, 32);
        }
    }

    [Fact]
    public void GetOrCreate_RecalculatesWhenRevisionChanges()
    {
        var cache = new StatusBarStatsCache();
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

        StatusBarCalculator.Stats CreateStats()
        {
            calls++;
            return new StatusBarCalculator.Stats(calls, calls, calls, calls, calls);
        }
    }

    [Fact]
    public void Clear_DropsCachedStats()
    {
        var cache = new StatusBarStatsCache();
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

        StatusBarCalculator.Stats CreateStats()
        {
            calls++;
            return new StatusBarCalculator.Stats(calls, calls, calls, calls, calls);
        }
    }
}
