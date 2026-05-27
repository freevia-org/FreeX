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

    [Fact]
    public void Build_SortsRecentAndPinnedItemsNewestFirst()
    {
        var now = DateTime.Now;
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\OldRecent.xlsx", LastOpened = now.AddDays(-4), IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\NewPinned.xlsx", LastOpened = now.AddMinutes(-5), IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\NewRecent.xlsx", LastOpened = now.AddMinutes(-10), IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\OldPinned.xlsx", LastOpened = now.AddDays(-3), IsPinned = true }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, filter: null);

        plan.AllItems.Select(item => item.FileName)
            .Should()
            .Equal("NewPinned.xlsx", "NewRecent.xlsx", "OldPinned.xlsx", "OldRecent.xlsx");
        plan.RecentItems.Select(item => item.FileName).Should().Equal("NewRecent.xlsx", "OldRecent.xlsx");
        plan.PinnedItems.Select(item => item.FileName).Should().Equal("NewPinned.xlsx", "OldPinned.xlsx");
    }

    [Fact]
    public void Build_RemovesMissingFilesBeforeSplittingRecentAndPinnedItems()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\MissingPinned.xlsx", LastOpened = DateTime.Now, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\ExistingPinned.xlsx", LastOpened = DateTime.Now, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\MissingRecent.xlsx", LastOpened = DateTime.Now, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\ExistingRecent.xlsx", LastOpened = DateTime.Now, IsPinned = false }
        };

        var plan = BackstageRecentFileListPlanner.Build(
            entries,
            filter: null,
            pathExists: path => !path.Contains("Missing", StringComparison.OrdinalIgnoreCase));

        plan.AllItems.Select(item => item.FileName).Should().Equal("ExistingRecent.xlsx", "ExistingPinned.xlsx");
        plan.PinnedItems.Select(item => item.FileName).Should().Equal("ExistingPinned.xlsx");
        plan.RecentItems.Select(item => item.FileName).Should().Equal("ExistingRecent.xlsx");
    }

    [Fact]
    public void Build_ProvidesUiAutomationTextForRecentPinnedAndRemoveCommands()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\Budget.xlsx", LastOpened = DateTime.Now, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\Forecast.xlsx", LastOpened = DateTime.Now, IsPinned = true }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, filter: null);

        var recent = plan.RecentItems.Single();
        recent.OpenAutomationName.Should().Be("Open recent file Budget.xlsx");
        recent.OpenAutomationHelpText.Should().Be(@"Open C:\Work\Budget.xlsx");
        recent.PinAutomationName.Should().Be("Pin Budget.xlsx");
        recent.PinAutomationHelpText.Should().Be("Keep this workbook in the pinned files list.");
        recent.RemoveAutomationName.Should().Be("Remove Budget.xlsx from recent files");
        recent.RemoveAutomationHelpText.Should().Be("Remove this workbook from the recent files list without deleting it.");

        var pinned = plan.PinnedItems.Single();
        pinned.OpenAutomationName.Should().Be("Open pinned file Forecast.xlsx");
        pinned.PinAutomationName.Should().Be("Unpin Forecast.xlsx");
        pinned.PinAutomationHelpText.Should().Be("Remove this workbook from the pinned files list.");
    }
}
