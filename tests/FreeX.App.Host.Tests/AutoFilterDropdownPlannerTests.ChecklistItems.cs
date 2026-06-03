using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownPlannerTests
{
    [Fact]
    public void CreateChecklistItems_ReturnsDistinctBodyValuesAndSkipsHeader()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(SheetId, 4, 1), new TextValue("apple"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 4, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterDropdownPlanner.CreateChecklistItems(sheet, plan);

        items.Should().Equal(
            new AutoFilterChecklistItem("Apple", "Apple"),
            new AutoFilterChecklistItem("Banana", "Banana"));
    }

    [Fact]
    public void CreateChecklistItems_UsesFilterColumnOffsetWithinCurrentRegion()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 2), new TextValue("Ignored Header"));
        sheet.SetCell(new CellAddress(SheetId, 1, 3), new TextValue("Status"));
        sheet.SetCell(new CellAddress(SheetId, 2, 2), new TextValue("Ignored"));
        sheet.SetCell(new CellAddress(SheetId, 2, 3), new TextValue("Open"));
        sheet.SetCell(new CellAddress(SheetId, 3, 2), new TextValue("Also Ignored"));
        sheet.SetCell(new CellAddress(SheetId, 3, 3), new TextValue("Closed"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 2),
                new CellAddress(SheetId, 3, 3)),
            FilterColumnOffset: 1);

        var items = AutoFilterDropdownPlanner.CreateChecklistItems(sheet, plan);

        items.Select(item => item.Value).Should().Equal("Open", "Closed");
    }

    [Fact]
    public void CreateChecklistItems_FormatsValuesLikeFilterCommandsAndRepresentsBlanksDistinctly()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(12.5));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(SheetId, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 19)));
        sheet.SetCell(new CellAddress(SheetId, 5, 1), ErrorValue.DivByZero);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 6, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterDropdownPlanner.CreateChecklistItems(sheet, plan);

        items.Should().Equal(
            new AutoFilterChecklistItem("12.5", "12.5"),
            new AutoFilterChecklistItem("TRUE", "TRUE"),
            new AutoFilterChecklistItem("2026-05-19", "2026-05-19"),
            new AutoFilterChecklistItem("#DIV/0!", "#DIV/0!"),
            new AutoFilterChecklistItem(UiText.Get("AutoFilter_BlankDisplayText"), ""));
    }
}
