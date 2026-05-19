using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SheetTabListPlannerTests
{
    [Fact]
    public void Build_EnsuresAtLeastOneVisibleSheetAndActiveVisibleSheet()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("Hidden1");
        var second = workbook.AddSheet("Hidden2");
        first.IsHidden = true;
        second.IsHidden = true;
        var grouped = new HashSet<SheetId>();

        var plan = SheetTabListPlanner.Build(workbook, second.Id, grouped);

        first.IsHidden.Should().BeFalse();
        plan.CurrentSheetId.Should().Be(first.Id);
        plan.Tabs.Should().ContainSingle().Which.Should().Match<SheetTabViewModel>(tab =>
            tab.Id == first.Id && tab.IsActive && tab.IsGrouped);
        grouped.Should().Equal(first.Id);
    }

    [Fact]
    public void Build_RemovesHiddenSheetsFromGroupedSet()
    {
        var workbook = new Workbook("Book");
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var grouped = new HashSet<SheetId> { visible.Id, hidden.Id };

        var plan = SheetTabListPlanner.Build(workbook, visible.Id, grouped);

        grouped.Should().Equal(visible.Id);
        plan.Tabs.Should().ContainSingle().Which.IsGrouped.Should().BeTrue();
    }

    [Fact]
    public void GenerateUniqueSheetName_SkipsExistingWorkbookNames()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        SheetTabListPlanner.GenerateUniqueSheetName(workbook).Should().Be("Sheet3");
    }

    [Fact]
    public void AdjacentVisibleSheet_ClampsToVisibleSheets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var hidden = workbook.AddSheet("Hidden");
        var second = workbook.AddSheet("Second");
        hidden.IsHidden = true;

        SheetTabListPlanner.AdjacentVisibleSheet(workbook, first.Id, 1).Should().Be(second.Id);
        SheetTabListPlanner.AdjacentVisibleSheet(workbook, second.Id, 1).Should().Be(second.Id);
        SheetTabListPlanner.AdjacentVisibleSheet(workbook, second.Id, -1).Should().Be(first.Id);
    }

    [Fact]
    public void AdjacentVisibleSheet_TreatsMissingCurrentAsFirstVisibleBeforeApplyingDirection()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");

        SheetTabListPlanner.AdjacentVisibleSheet(workbook, SheetId.New(), 1).Should().Be(second.Id);
    }

    [Fact]
    public void SelectAdjacentVisibleSheetGroup_ExtendsFromAnchorAcrossVisibleSheets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var hidden = workbook.AddSheet("Hidden");
        var third = workbook.AddSheet("Third");
        hidden.IsHidden = true;

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            first.Id,
            anchorSheetId: null,
            direction: 1);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(second.Id);
        plan.AnchorSheetId.Should().Be(first.Id);
        plan.GroupedSheetIds.Should().Equal(first.Id, second.Id);

        var extended = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            plan.CurrentSheetId,
            plan.AnchorSheetId,
            direction: 1);

        extended.Should().NotBeNull();
        extended!.CurrentSheetId.Should().Be(third.Id);
        extended.AnchorSheetId.Should().Be(first.Id);
        extended.GroupedSheetIds.Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void SelectAdjacentVisibleSheetGroup_ClampsAtWorkbookEdges()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            first.Id,
            anchorSheetId: null,
            direction: -1);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(first.Id);
        plan.GroupedSheetIds.Should().Equal(first.Id);
    }
}
