using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageHomePanePlannerTests
{
    [Fact]
    public void Build_PinsRecentTabsSearchColumnsRowsAndCommands()
    {
        var plan = FreeXBackstageHomePanePlanner.Build();

        plan.RecentTab.Should().Be(new FreeXBackstageRecentTabDescriptor(
            FreeXBackstageRecentTabId.Recent,
            "MainWindow_Text_Recent",
            "MainWindow_TooltipTitle_Recent",
            "RC",
            "Recent"));
        plan.PinnedTab.Should().Be(new FreeXBackstageRecentTabDescriptor(
            FreeXBackstageRecentTabId.Pinned,
            "MainWindow_Text_Pinned",
            "MainWindow_TooltipTitle_Pinned",
            "PN",
            "Pinned"));

        plan.Search.Should().Be(new FreeXBackstageRecentSearchDescriptor(
            "MainWindow_AutomationName_SearchRecentFiles",
            "MainWindow_AutomationHelpText_FilterRecentAndPinnedFiles"));
        plan.Columns.Select(column => (column.Id, column.LabelKey)).Should().Equal(
            (FreeXBackstageRecentColumnId.Name, "MainWindow_Text_Name"),
            (FreeXBackstageRecentColumnId.DateModified, "MainWindow_Text_DateModified"));
        plan.Rows.Select(row => (row.Kind, row.AutomationId)).Should().Equal(
            (FreeXBackstageRecentFileRowKind.Recent, "BackstageRecentFileItem"),
            (FreeXBackstageRecentFileRowKind.Pinned, "BackstagePinnedFileItem"));
        plan.RowCommands.Select(command => (command.Id, command.AutomationId, command.CommandName, command.IconCommandName))
            .Should().Equal(
                (FreeXBackstageRecentFileCommandId.Pin, "BackstageRecentPinButton", "Pin File", "Pin to list"),
                (FreeXBackstageRecentFileCommandId.Unpin, "BackstagePinnedUnpinButton", "Unpin File", "Unpin from list"));
    }

    [Theory]
    [InlineData(FreeXBackstageRecentTabId.Recent, false, 0, "Backstage_Home_NoRecentWorkbooks")]
    [InlineData(FreeXBackstageRecentTabId.Pinned, false, 0, "Backstage_Home_NoPinnedWorkbooks")]
    [InlineData(FreeXBackstageRecentTabId.Recent, true, 0, "Backstage_Home_NoMatchingWorkbooks")]
    [InlineData(FreeXBackstageRecentTabId.Pinned, true, 0, "Backstage_Home_NoMatchingWorkbooks")]
    [InlineData(FreeXBackstageRecentTabId.Recent, false, 3, null)]
    [InlineData(FreeXBackstageRecentTabId.Pinned, true, 1, null)]
    public void ResolveEmptyStateKey_ExplainsEmptyListsAndStaysQuietWhenRowsExist(
        FreeXBackstageRecentTabId tab,
        bool hasFilter,
        int rowCount,
        string? expected)
    {
        FreeXBackstageHomePanePlanner.ResolveEmptyStateKey(tab, hasFilter, rowCount).Should().Be(expected);
    }
}
