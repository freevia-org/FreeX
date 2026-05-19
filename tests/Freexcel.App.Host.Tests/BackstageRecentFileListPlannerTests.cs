using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class BackstageRecentFileListPlannerTests
{
    [Fact]
    public void Build_SplitsPinnedAndUnpinnedItemsAfterFiltering()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\Budget.xlsx", LastOpened = DateTime.Now, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\Forecast.xlsx", LastOpened = DateTime.Now, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\Notes.xlsx", LastOpened = DateTime.Now, IsPinned = true }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, "cast");

        plan.AllItems.Select(item => item.FileName).Should().Equal("Forecast.xlsx");
        plan.RecentItems.Should().BeEmpty();
        plan.PinnedItems.Select(item => item.FileName).Should().Equal("Forecast.xlsx");
    }

    [Fact]
    public void Build_FiltersByFileNameAndDirectoryCaseInsensitively()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Finance\Budget.xlsx", LastOpened = DateTime.Now, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Ops\Runbook.xlsx", LastOpened = DateTime.Now, IsPinned = false }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, "finance");

        plan.RecentItems.Select(item => item.FileName).Should().Equal("Budget.xlsx");
    }
}
