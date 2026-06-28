using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterDropdownMenuPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly TestTextProvider Text = new();

    [Fact]
    public void TryPlan_ReturnsCurrentRegionAndColumnOffsetForHeaderCell()
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));
        var activeCell = new CellAddress(SheetId, 2, 5);

        var planned = AutoFilterDropdownMenuPlanner.TryPlan(region, activeCell, out var plan);

        planned.Should().BeTrue();
        plan.Range.Should().Be(region);
        plan.FilterColumnOffset.Should().Be(2);
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

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.HeaderText.Should().Be("Fruit");
        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        menu.Entries.Select(entry => entry.Header).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Clear Filter from Fruit",
            "Text Filters",
            "Search",
            "(Select All)",
            "Apple",
            "Banana");
        menu.Sections.Select(section => section.Kind).Should().Equal(
            AutoFilterMenuSectionKind.Sort,
            AutoFilterMenuSectionKind.FilterCommands,
            AutoFilterMenuSectionKind.Search,
            AutoFilterMenuSectionKind.Checklist);
    }

    [Fact]
    public void CreateMenuPlan_IncludesColorOptions_WhenWorkbookStylesHaveColors()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));
        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = new CellColor(0x21, 0x73, 0x46);
        var fillStyleId = workbook.RegisterStyle(fillStyle);
        sheet.GetCell(2, 1)!.StyleId = fillStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().Contain(option =>
            option.Kind == AutoFilterColorFilterKind.CellFillColor &&
            option.Label == "#217346");
        menu.ColorOptions.Should().Contain(option => option.Kind == AutoFilterColorFilterKind.NoFill);
        menu.Entries.Should().Contain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
    }

    [Fact]
    public void HasActiveFilter_DetectsFilteredDataRowsInsideRange()
    {
        var sheet = CreateSheetWithList();
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));

        sheet.FilterHiddenRows.Add(3);

        AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range).Should().BeTrue();
    }

    [Fact]
    public void HasActiveFilter_IgnoresHeaderAndRowsOutsideRange()
    {
        var sheet = CreateSheetWithList();
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));

        sheet.FilterHiddenRows.UnionWith([1u, 8u]);

        AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range).Should().BeFalse();
    }

    private static Sheet CreateSheetWithList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey switch
        {
            "AutoFilter_SortAscending" => "Sort A to Z",
            "AutoFilter_SortDescending" => "Sort Z to A",
            "AutoFilter_FilterByColor" => "Filter by Color",
            "AutoFilter_Search" => "Search",
            "AutoFilter_SelectAll" => "(Select All)",
            "AutoFilter_NoFill" => "No Fill",
            "AutoFilter_FilterFamily_Text" => "Text Filters",
            "AutoFilter_FilterFamily_Number" => "Number Filters",
            "AutoFilter_FilterFamily_Date" => "Date Filters",
            "AutoFilter_SectionSort" => "Sort",
            "AutoFilter_SectionFilter" => "Filter",
            "AutoFilter_SectionSearch" => "Search",
            "AutoFilter_SectionValues" => "Values",
            "AutoFilter_Criteria_Equals" => "Equals",
            "AutoFilter_Criteria_DoesNotEqual" => "Does Not Equal",
            "AutoFilter_Criteria_GreaterThan" => "Greater Than",
            "AutoFilter_Criteria_GreaterThanOrEqualTo" => "Greater Than or Equal To",
            "AutoFilter_Criteria_LessThan" => "Less Than",
            "AutoFilter_Criteria_LessThanOrEqualTo" => "Less Than or Equal To",
            "AutoFilter_Criteria_Between" => "Between",
            "AutoFilter_Criteria_Top10" => "Top 10",
            "AutoFilter_Criteria_Bottom10" => "Bottom 10",
            "AutoFilter_Criteria_Top10Percent" => "Top 10%",
            "AutoFilter_Criteria_Bottom10Percent" => "Bottom 10%",
            "AutoFilter_Criteria_AboveAverage" => "Above Average",
            "AutoFilter_Criteria_BelowAverage" => "Below Average",
            "AutoFilter_Criteria_Blanks" => "Blanks",
            "AutoFilter_Criteria_NonBlanks" => "NonBlanks",
            "AutoFilter_Criteria_After" => "After",
            "AutoFilter_Criteria_OnOrAfter" => "On or After",
            "AutoFilter_Criteria_Before" => "Before",
            "AutoFilter_Criteria_OnOrBefore" => "On or Before",
            "AutoFilter_Criteria_Contains" => "Contains",
            "AutoFilter_Criteria_DoesNotContain" => "Does Not Contain",
            "AutoFilter_Criteria_BeginsWith" => "Begins With",
            "AutoFilter_Criteria_EndsWith" => "Ends With",
            _ => resourceKey
        };

        public string Format(string resourceKey, string value) => resourceKey switch
        {
            "AutoFilter_ClearFilterFrom" => $"Clear Filter from {value}",
            "AutoFilter_ColumnHeader" => $"Column {value}",
            _ => $"{resourceKey}: {value}"
        };
    }
}
