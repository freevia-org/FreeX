using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AutoFilterDropdownPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryPlan_ReturnsCurrentRegionAndColumnOffsetForHeaderCell()
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));
        var activeCell = new CellAddress(SheetId, 2, 5);

        var planned = AutoFilterDropdownPlanner.TryPlan(region, activeCell, out var plan);

        planned.Should().BeTrue();
        plan.Range.Should().Be(region);
        plan.FilterColumnOffset.Should().Be(2);
    }

    [Theory]
    [InlineData(3u, 5u)]
    [InlineData(2u, 7u)]
    [InlineData(1u, 5u)]
    public void TryPlan_RejectsCellsOutsideHeaderRowOrRegion(uint row, uint col)
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));

        AutoFilterDropdownPlanner.TryPlan(region, new CellAddress(SheetId, row, col), out _)
            .Should()
            .BeFalse();
    }

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
            new AutoFilterChecklistItem("(Blanks)", ""));
    }

    [Fact]
    public void CreateMenuPlan_BuildsExcelStyleTextFilterMenuSections()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

        menu.HeaderText.Should().Be("Fruit");
        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        menu.Entries.Select(entry => entry.Header).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Clear Filter From \"Fruit\"",
            "Filter by Color",
            "Text Filters",
            "Search",
            "(Select All)",
            "Apple",
            "Banana");
        menu.Entries.Single(entry => entry.Header == "Text Filters")
            .CriteriaSuggestions.Should().Equal("equals:", "text<>", "contains:", "notcontains:", "begins:", "ends:", "blank", "nonblank");
    }

    [Fact]
    public void CreateMenuPlan_IncludesExcelStyleSectionSeparators()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

        menu.Entries.Select(entry => entry.Kind).Should().ContainInOrder(
            AutoFilterMenuEntryKind.SortAscending,
            AutoFilterMenuEntryKind.SortDescending,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.ClearFilter,
            AutoFilterMenuEntryKind.FilterByColor,
            AutoFilterMenuEntryKind.FilterFamily,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.Search,
            AutoFilterMenuEntryKind.SelectAll,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.ChecklistItem,
            AutoFilterMenuEntryKind.ChecklistItem);
    }

    [Fact]
    public void CreateMenuPlan_ChoosesNumberAndDateFilterFamiliesFromBodyValues()
    {
        var numberSheet = new Sheet(SheetId, "Sheet1");
        numberSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Amount"));
        numberSheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(10));
        numberSheet.SetCell(new CellAddress(SheetId, 3, 1), new NumberValue(20));
        var numberPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var dateSheet = new Sheet(SheetId, "Sheet1");
        dateSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Due"));
        dateSheet.SetCell(new CellAddress(SheetId, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        dateSheet.SetCell(new CellAddress(SheetId, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 21)));
        var datePlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .FilterKind.Should().Be(AutoFilterMenuFilterKind.Number);
        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .Entries.Single(entry => entry.Header == "Number Filters")
            .CriteriaSuggestions.Should().Equal("=", "<>", ">", ">=", "<", "<=", "between:", "top:", "bottom:", "toppercent:", "bottompercent:", "above average", "below average", "blank", "nonblank");

        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .FilterKind.Should().Be(AutoFilterMenuFilterKind.Date);
        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .Entries.Single(entry => entry.Header == "Date Filters")
            .CriteriaSuggestions.Should().Equal("date=", "date<>", "date>", "date>=", "date<", "date<=", "datebetween:", "blank", "nonblank");
    }

    [Fact]
    public void CreateMenuPlan_OffersBlankAndNonblankCriteriaForEveryFilterFamily()
    {
        var textSheet = new Sheet(SheetId, "Sheet1");
        textSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Name"));
        textSheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Quinn"));
        var textPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        var numberSheet = new Sheet(SheetId, "Sheet1");
        numberSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Amount"));
        numberSheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(10));
        var numberPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        var dateSheet = new Sheet(SheetId, "Sheet1");
        dateSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Due"));
        dateSheet.SetCell(new CellAddress(SheetId, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        var datePlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(textSheet, textPlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
    }

    [Fact]
    public void CreateMenuPlan_CollectsDistinctColumnFillAndFontColorsForFilterByColorMenu()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        var green = new CellColor(0, 176, 80);
        var yellow = new CellColor(255, 192, 0);
        var red = new CellColor(192, 0, 0);
        var greenStyle = CellStyle.Default.Clone();
        greenStyle.FillColor = green;
        var yellowStyle = CellStyle.Default.Clone();
        yellowStyle.FillColor = yellow;
        yellowStyle.FontColor = red;
        var greenStyleId = workbook.RegisterStyle(greenStyle);
        var yellowStyleId = workbook.RegisterStyle(yellowStyle);

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Blocked"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("Closed"));
        sheet.GetCell(2, 1)!.StyleId = greenStyleId;
        sheet.GetCell(3, 1)!.StyleId = yellowStyleId;
        sheet.GetCell(4, 1)!.StyleId = greenStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(workbook, sheet, plan);

        menu.ColorOptions.Should().Equal(
            new AutoFilterColorOption("#00B050", AutoFilterColorFilterKind.CellFillColor, green),
            new AutoFilterColorOption("#FFC000", AutoFilterColorFilterKind.CellFillColor, yellow),
            new AutoFilterColorOption("No Fill", AutoFilterColorFilterKind.NoFill, null),
            new AutoFilterColorOption("#C00000", AutoFilterColorFilterKind.FontColor, red));
    }

    [Fact]
    public void CreateMenuPlan_OmitsColorChoicesWhenWorkbookIsUnavailable()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan)
            .ColorOptions.Should().BeEmpty();
    }
}
