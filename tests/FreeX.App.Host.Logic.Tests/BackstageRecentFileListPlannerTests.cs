using System.IO;
using System.Text.Json;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Services;

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
    public void SelectPinnedFirst_ComposesCappedLiveBackstageRows()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = BackstageRecentFileListPlanner.Build(
            new[]
            {
                new RecentFileEntry { Path = @"C:\Work\Recent.xlsx", LastOpened = now },
                new RecentFileEntry { Path = @"C:\Work\OlderPinned.xlsx", LastOpened = now.AddDays(-2), IsPinned = true },
                new RecentFileEntry { Path = @"C:\Work\NewerPinned.xlsx", LastOpened = now.AddDays(-1), IsPinned = true },
            },
            filter: null);

        var rows = BackstageRecentFileListPlanner.SelectPinnedFirst(plan, maximumCount: 2);

        rows.Select(item => item.FileName).Should().Equal("NewerPinned.xlsx", "OlderPinned.xlsx");
    }

    [Fact]
    public void Build_PreservesPortableFileAccessIdentityForRendererOpenAction()
    {
        var identity = new WorkbookFileAccessIdentity(
            @"C:\Work\Budget.xlsx",
            bookmarkKind: "security-scoped",
            bookmarkPayload: "bookmark");
        var plan = BackstageRecentFileListPlanner.Build(
            [new RecentFileEntry { Path = identity.LocalPath, FileAccessIdentity = identity }],
            filter: null);

        plan.AllItems.Single().FileAccessIdentity.Should().BeSameAs(identity);
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("shared", "Free.Shared.Shell", "BackstageRecentFileListPlanner.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("shared", "Free.Shared.AppServices", "RecentFilesStore.cs"));

        source.Should().Contain("DateTimeOffset.UtcNow");
        source.Should().Contain("LastOpened = _clock()");
        source.Should().NotContain("LastOpened = DateTime.Now");
    }

    [Fact]
    public void RecentFilesStore_LimitForPersistence_PreservesPinnedFilesBeyondRecentLimit()
    {
        var entries = Enumerable.Range(0, RecentFilesStore.MaxRecentEntries)
            .Select(index => new RecentFileEntry
            {
                Path = $@"C:\Work\Recent{index:00}.xlsx",
                LastOpened = DateTimeOffset.UtcNow.AddMinutes(-index)
            })
            .Append(new RecentFileEntry
            {
                Path = @"C:\Work\PinnedOld.xlsx",
                LastOpened = DateTimeOffset.UtcNow.AddDays(-30),
                IsPinned = true
            })
            .Append(new RecentFileEntry
            {
                Path = @"C:\Work\DroppedOld.xlsx",
                LastOpened = DateTimeOffset.UtcNow.AddDays(-31)
            });

        var limited = RecentFilesStore.LimitForPersistence(entries);

        limited.Select(entry => entry.Path).Should().Contain(@"C:\Work\PinnedOld.xlsx");
        limited.Select(entry => entry.Path).Should().NotContain(@"C:\Work\DroppedOld.xlsx");
        limited.Count(entry => !entry.IsPinned).Should().Be(RecentFilesStore.MaxRecentEntries);
    }

    [Fact]
    public void RecentFilesStore_LimitForPersistence_PreservesEntryOrder()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = @"C:\Work\New.xlsx" },
            new RecentFileEntry { Path = @"C:\Work\Pinned.xlsx", IsPinned = true },
            new RecentFileEntry { Path = @"C:\Work\Old.xlsx" }
        };

        var limited = RecentFilesStore.LimitForPersistence(entries, maxRecentEntries: 1);

        limited.Select(entry => entry.Path).Should().Equal(@"C:\Work\New.xlsx", @"C:\Work\Pinned.xlsx");
    }

    [Fact]
    public void RecentFilesStore_AddOrUpdate_DoesNotEvictPinnedEntriesWhenTrimming()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var timestamp = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var store = new RecentFilesStore(storePath, () => timestamp);
        var pinnedPath = @"C:\Work\Recent00.xlsx";

        for (var i = 0; i < RecentFilesStore.MaxRecentEntries; i++)
        {
            timestamp = timestamp.AddMinutes(1);
            store.AddOrUpdate($@"C:\Work\Recent{i:00}.xlsx");
        }

        store.Pin(pinnedPath);

        for (var i = RecentFilesStore.MaxRecentEntries; i < RecentFilesStore.MaxRecentEntries + 6; i++)
        {
            timestamp = timestamp.AddMinutes(1);
            store.AddOrUpdate($@"C:\Work\Recent{i:00}.xlsx");
        }

        store.Entries.Count(entry => !entry.IsPinned).Should().Be(RecentFilesStore.MaxRecentEntries);
        store.Entries.Should().Contain(entry => entry.Path == pinnedPath && entry.IsPinned);
        store.Entries.Should().NotContain(entry => entry.Path == @"C:\Work\Recent01.xlsx");

        var reloaded = RecentFilesStore.Load(storePath);
        reloaded.Entries.Should().Contain(entry => entry.Path == pinnedPath && entry.IsPinned);
    }

    [Fact]
    public void RecentFilesStore_Load_TrimsOverflowWithoutDroppingPinnedEntries()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var entries = new List<RecentFileEntry>();
        var timestamp = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var pinnedPath = @"C:\Work\Pinned.xlsx";
        entries.Add(new RecentFileEntry { Path = pinnedPath, LastOpened = timestamp, IsPinned = true });
        for (var i = 0; i < RecentFilesStore.MaxRecentEntries + 5; i++)
        {
            entries.Add(new RecentFileEntry
            {
                Path = $@"C:\Work\Recent{i:00}.xlsx",
                LastOpened = timestamp.AddMinutes(i + 1),
                IsPinned = false
            });
        }

        File.WriteAllText(storePath, JsonSerializer.Serialize(entries));

        var store = RecentFilesStore.Load(storePath);

        store.Entries.Count(entry => !entry.IsPinned).Should().Be(RecentFilesStore.MaxRecentEntries);
        store.Entries.Should().Contain(entry => entry.Path == pinnedPath && entry.IsPinned);
        store.Entries.Should().NotContain(entry => entry.Path == @"C:\Work\Recent25.xlsx");
    }
}
