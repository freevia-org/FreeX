using System.IO;
using System.Text.Json;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class BackstageRecentFileListPlannerTests
{
    [Fact]
    public void Build_SplitsPinnedAndUnpinnedItemsAfterFiltering()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\Budget.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\Forecast.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\Notes.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = true }
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
            new RecentFileEntry { Path = @"C:\Finance\Budget.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Ops\Runbook.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, "finance");

        plan.RecentItems.Select(item => item.FileName).Should().Equal("Budget.xlsx");
    }

    [Fact]
    public void Build_SortsRecentAndPinnedItemsNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
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
            new RecentFileEntry { Path = @"C:\Work\MissingPinned.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\ExistingPinned.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\MissingRecent.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\ExistingRecent.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false }
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
    public void Build_AvoidsLinqPipelinesForRecentFileHotPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "BackstageRecentFileListPlanner.cs"));

        source.Should().NotContain(".Where(");
        source.Should().NotContain(".OrderByDescending(");
        source.Should().NotContain(".Select(");
        source.Should().NotContain(".ToList(");
    }

    [Fact]
    public void Build_ProvidesUiAutomationTextForRecentPinnedAndRemoveCommands()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\Budget.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = false },
            new RecentFileEntry { Path = @"C:\Work\Forecast.xlsx", LastOpened = DateTimeOffset.UtcNow, IsPinned = true }
        };

        var plan = BackstageRecentFileListPlanner.Build(entries, filter: null);

        var recent = plan.RecentItems.Single();
        recent.OpenAutomationName.Should().Be(UiText.Format("Backstage_Recent_OpenRecentFileAutomationName", "Budget.xlsx"));
        recent.OpenAutomationHelpText.Should().Be(UiText.Format("Backstage_Recent_OpenAutomationHelpText", @"C:\Work\Budget.xlsx"));
        recent.PinAutomationName.Should().Be(UiText.Format("Backstage_Recent_PinAutomationName", "Budget.xlsx"));
        recent.PinAutomationHelpText.Should().Be(UiText.Get("Backstage_Recent_PinHelpText"));
        recent.RemoveAutomationName.Should().Be(UiText.Format("Backstage_Recent_RemoveAutomationName", "Budget.xlsx"));
        recent.RemoveAutomationHelpText.Should().Be(UiText.Get("Backstage_Recent_RemoveAutomationHelpText"));

        var pinned = plan.PinnedItems.Single();
        pinned.OpenAutomationName.Should().Be(UiText.Format("Backstage_Recent_OpenPinnedFileAutomationName", "Forecast.xlsx"));
        pinned.PinAutomationName.Should().Be(UiText.Format("Backstage_Recent_UnpinAutomationName", "Forecast.xlsx"));
        pinned.PinAutomationHelpText.Should().Be(UiText.Get("Backstage_Recent_UnpinHelpText"));
    }

    [Fact]
    public void RecentFileEntry_SerializesLastOpenedWithUtcOffset()
    {
        var entry = new RecentFileEntry
        {
            Path = @"C:\Work\Budget.xlsx",
            LastOpened = new DateTimeOffset(2026, 5, 28, 12, 34, 56, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(entry);

        json.Should().Contain(@"""LastOpened"":""2026-05-28T12:34:56+00:00""");
    }

    [Fact]
    public void RecentFilesStore_UsesUtcClockForPersistedTimestamps()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RecentFilesStore.cs"));

        source.Should().Contain("LastOpened = DateTimeOffset.UtcNow");
        source.Should().NotContain("LastOpened = DateTime.Now");
    }
}
