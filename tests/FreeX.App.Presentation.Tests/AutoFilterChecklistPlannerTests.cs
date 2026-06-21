using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class AutoFilterChecklistPlannerTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    [Fact]
    public void ToFilterText_UsesCanonicalFilterFormatting()
    {
        AutoFilterChecklistPlanner.ToFilterText(new TextValue("Hi")).Should().Be("Hi");
        AutoFilterChecklistPlanner.ToFilterText(new NumberValue(10)).Should().Be("10");
        AutoFilterChecklistPlanner.ToFilterText(new BoolValue(true)).Should().Be("TRUE");
        AutoFilterChecklistPlanner.ToFilterText(new BoolValue(false)).Should().Be("FALSE");
        AutoFilterChecklistPlanner.ToFilterText(DateTimeValue.FromDateTime(new DateTime(2026, 1, 2)))
            .Should().Be("2026-01-02");
        AutoFilterChecklistPlanner.ToFilterText(BlankValue.Instance).Should().Be("");
        AutoFilterChecklistPlanner.ToFilterText(ErrorValue.DivByZero).Should().Be("#DIV/0!");
    }

    [Fact]
    public void DistinctColumnValues_ExcludesHeader_HonorsOffset_AndKeepsFirstSeenOrder()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 1);
        var values = AutoFilterChecklistPlanner.DistinctColumnValues(sheet, plan.Range, plan.FilterColumnOffset);
        var items = AutoFilterChecklistPlanner.CreateItems(sheet, plan, blankDisplayText: "(Blanks)");

        values.Should().Equal("20", "10");
        items.Select(item => item.Value).Should().Equal("10", "20");
    }

    [Fact]
    public void CreateItems_DeduplicatesCaseInsensitive_SortsByExcelLikeRank_AndLabelsBlank()
    {
        var items = AutoFilterChecklistPlanner.CreateItems(
            ["banana", "10", "2", "apple", "", "BANANA", null, "2026-01-02", "TRUE", "#DIV/0!"],
            blankDisplayText: "(Blanks)");

        items.Should().Equal(
            new AutoFilterChecklistItem("2", "2"),
            new AutoFilterChecklistItem("10", "10"),
            new AutoFilterChecklistItem("2026-01-02", "2026-01-02"),
            new AutoFilterChecklistItem("apple", "apple"),
            new AutoFilterChecklistItem("banana", "banana"),
            new AutoFilterChecklistItem("TRUE", "TRUE"),
            new AutoFilterChecklistItem("#DIV/0!", "#DIV/0!"),
            new AutoFilterChecklistItem("(Blanks)", ""));
    }
}
