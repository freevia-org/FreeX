using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R76-render-autofilter-dropdown-4-2: Excel offers "Sort by Color" alongside "Filter by Color"
/// whenever a column has fill/font colors -- these tests cover the dropdown-plan entry and the
/// SortCommand it produces.
/// </summary>
public sealed class R76_AutoFilterSortByColorTests
{
    private static readonly TestTextProvider Text = new();

    [Fact]
    public void CreateMenuPlan_ColumnWithFillColors_OffersSortByColorEntry()
    {
        var (workbook, sheet, plan) = CreateColoredColumn();

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.Entries.Should().Contain(entry => entry.Kind == AutoFilterMenuEntryKind.SortByColor);
        menu.Entries.Should().Contain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
    }

    [Fact]
    public void CreateMenuPlan_ColumnWithNoColors_OffersNeitherSortByColorNorFilterByColor()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.Entries.Should().NotContain(entry => entry.Kind == AutoFilterMenuEntryKind.SortByColor);
        menu.Entries.Should().NotContain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
    }

    [Fact]
    public void CreateSortByColorCommand_MovesMatchingColorRowsToTop()
    {
        var (workbook, sheet, plan) = CreateColoredColumn();
        var greenOption = new AutoFilterColorOption("#217346", AutoFilterColorFilterKind.CellFillColor, new CellColor(0x21, 0x73, 0x46));

        // SortCommand itself has no header concept -- it sorts every row of the range it is given
        // (the caller is responsible for excluding the header row, same as the existing Sort A-Z/
        // Z-A AutoFilter path). Use the data-only sub-range here to isolate the color-sort behavior
        // itself from that unrelated range-slicing concern.
        var dataOnlyRange = new GridRange(
            new CellAddress(sheet.Id, plan.Range.Start.Row + 1, plan.Range.Start.Col),
            plan.Range.End);
        var command = AutoFilterDropdownMenuPlanner.CreateSortByColorCommand(
            sheet.Id, dataOnlyRange, plan.FilterColumnOffset, greenOption);

        var context = new TestCommandContext(workbook);
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue();
        // Row 3 ("Banana") had the green fill; it must now sit first among the data rows.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Banana"));
    }

    [Fact]
    public void CreateSortByColorCommand_ThrowsForNoFillOption_HasNoSingleTargetColor()
    {
        var (_, sheet, plan) = CreateColoredColumn();
        var noFillOption = new AutoFilterColorOption("No Fill", AutoFilterColorFilterKind.NoFill, null);

        var act = () => AutoFilterDropdownMenuPlanner.CreateSortByColorCommand(
            sheet.Id, plan.Range, plan.FilterColumnOffset, noFillOption);

        act.Should().Throw<ArgumentException>();
    }

    private static (Workbook Workbook, Sheet Sheet, AutoFilterDropdownPlan Plan) CreateColoredColumn()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));

        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = new CellColor(0x21, 0x73, 0x46);
        sheet.GetCell(3, 1)!.StyleId = workbook.RegisterStyle(fillStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);
        return (workbook, sheet, plan);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey switch
        {
            "AutoFilter_SortAscending" => "Sort A to Z",
            "AutoFilter_SortDescending" => "Sort Z to A",
            "AutoFilter_SortAToZ" => "Sort A to Z",
            "AutoFilter_SortZToA" => "Sort Z to A",
            "AutoFilter_FilterByColor" => "Filter by Color",
            "AutoFilter_SortByColor" => "Sort by Color",
            "AutoFilter_Search" => "Search",
            "AutoFilter_SelectAll" => "(Select All)",
            "AutoFilter_NoFill" => "No Fill",
            "AutoFilter_FilterFamily_Text" => "Text Filters",
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
