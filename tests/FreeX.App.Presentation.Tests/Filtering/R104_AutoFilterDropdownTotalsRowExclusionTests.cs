using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R104-app-presentation-autofilter-totalsrow-1: a structured table's raw <c>Range</c> (the shape
/// <see cref="AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange"/>/plan.Range carries whenever the
/// dropdown targets a filtered table) has its Totals Row as <c>range.End.Row</c> when Table Design >
/// Total Row is on. The dropdown-menu builder (kind detection, checklist, color list) must exclude
/// that row from the filterable data set exactly like the interactive filter-apply commands
/// (FilterCommand/TopBottomFilterCommand/AverageFilterCommand/FilterConditionCommand) already do via
/// StructuredTableEditEffects.GetFilterableLastRow.
/// </summary>
public sealed class R104_AutoFilterDropdownTotalsRowExclusionTests
{
    private static readonly TestTextProvider Text = new();

    [Fact]
    public void CreateMenuPlan_DetectFilterKind_ExcludesShownTotalsRowValue()
    {
        // Single-column table: data rows are numbers, but the shown Totals Row cell in this SAME
        // column carries a literal text label ("Total") -- exactly the shape Commands.cs's own
        // GetFilterableLastRow doc comment describes. Real Excel never lets a Totals Row's text
        // sway Number/Date/Text kind detection for the column.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total")); // shown Totals Row

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = range,
            HasAutoFilter = true,
            TotalsRowShown = true,
            HeaderRowCount = 1
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Number);
    }

    [Fact]
    public void CreateMenuPlan_Checklist_ExcludesShownTotalsRowValue()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total")); // shown Totals Row

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = range,
            HasAutoFilter = true,
            TotalsRowShown = true,
            HeaderRowCount = 1
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        var checklistValues = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => entry.Value)
            .ToList();

        checklistValues.Should().Equal("Apple", "Banana");
        checklistValues.Should().NotContain("Total");
    }

    [Fact]
    public void CreateMenuPlan_ColorOptions_ExcludeShownTotalsRowFillColor()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30)); // shown Totals Row (SUBTOTAL result)

        var redFillStyle = CellStyle.Default.Clone();
        redFillStyle.FillColor = new CellColor(0xFF, 0x00, 0x00);
        var redStyleId = workbook.RegisterStyle(redFillStyle);
        // Only the Totals Row cell is colored -- no data row carries this fill.
        sheet.GetCell(4, 1)!.StyleId = redStyleId;

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = range,
            HasAutoFilter = true,
            TotalsRowShown = true,
            HeaderRowCount = 1
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().NotContain(option =>
            option.Kind == AutoFilterColorFilterKind.CellFillColor && option.Label == "#FF0000");
        // Both real data rows have no fill, so "No Fill" should NOT be offered either (there is no
        // colored data row to contrast it against) -- confirming the totals row never entered the
        // scan at all rather than merely being reclassified.
        menu.ColorOptions.Should().BeEmpty();
    }

    [Fact]
    public void CreateMenuPlan_ChecklistCheckedState_IgnoresShownTotalsRowWhenScanningOwnedHiddenRows()
    {
        // Column-owned hidden-row filter (as TopBottom/Condition/color filters leave behind): row 3
        // (Banana) is hidden and owned by this column's own filter mechanism. The shown Totals Row
        // (row 5) happens to carry the literal text "Banana" (e.g. a custom Totals Row label) --
        // reopening the dropdown must not let that unrelated Totals Row cell make the checklist
        // think an actual hidden data value ("Banana") is still visible.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Cherry"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Banana")); // shown Totals Row

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = range,
            HasAutoFilter = true,
            TotalsRowShown = true,
            HeaderRowCount = 1
        });

        sheet.ColumnFilterOwnedRows[1] = [3u];
        sheet.FilterHiddenRows.Add(3);

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        var checkedByValue = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked);

        checkedByValue.Should().BeEquivalentTo(new Dictionary<string, bool?>
        {
            ["Apple"] = true,
            ["Banana"] = false,
            ["Cherry"] = true
        });
    }

    [Fact]
    public void CreateMenuPlan_TotalsRowNotShown_StillIncludesLastDataRow()
    {
        // No-regression sibling: when the table's Totals Row is OFF, range.End.Row is a genuine data
        // row and must still be scanned in full (this is the same table shape minus TotalsRowShown).
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = range,
            HasAutoFilter = true,
            TotalsRowShown = false,
            HeaderRowCount = 1
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        var checklistValues = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => entry.Value)
            .ToList();

        checklistValues.Should().Equal("Apple", "Banana");
    }

    [Fact]
    public void CreateMenuPlan_PlainWorksheetRange_StillIncludesLastRow()
    {
        // No-regression sibling: a plain worksheet-level AutoFilter range (no matching structured
        // table) must still scan through range.End.Row unchanged -- GetFilterableLastRow only
        // shortens the bound when range is exactly a table's own Range.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        var checklistValues = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => entry.Value)
            .ToList();

        checklistValues.Should().Equal("Apple", "Banana");
    }

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey switch
        {
            "AutoFilter_SortAscending" => "Sort A to Z",
            "AutoFilter_SortDescending" => "Sort Z to A",
            "AutoFilter_SortAToZ" => "Sort A to Z",
            "AutoFilter_SortZToA" => "Sort Z to A",
            "AutoFilter_SortSmallestToLargest" => "Sort Smallest to Largest",
            "AutoFilter_SortLargestToSmallest" => "Sort Largest to Smallest",
            "AutoFilter_SortOldestToNewest" => "Sort Oldest to Newest",
            "AutoFilter_SortNewestToOldest" => "Sort Newest to Oldest",
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
