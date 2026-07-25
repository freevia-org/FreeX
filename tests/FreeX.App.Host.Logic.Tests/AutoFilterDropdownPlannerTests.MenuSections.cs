using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownMenuPlannerHostResourceTests
{
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

        var menu = CreateMenuPlan(sheet, plan);

        menu.HeaderText.Should().Be("Fruit");
        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        menu.Entries.Select(entry => entry.Header).Should().ContainInOrder(
            UiText.Get("AutoFilter_SortAToZ"),
            UiText.Get("AutoFilter_SortZToA"),
            UiText.Format("AutoFilter_ClearFilterFrom", "Fruit"),
            UiText.Get("AutoFilter_FilterFamily_Text"),
            UiText.Get("AutoFilter_Search"),
            UiText.Get("AutoFilter_SelectAll"),
            "Apple",
            "Banana");
        menu.Entries.Single(entry => entry.Header == UiText.Get("AutoFilter_FilterFamily_Text"))
            .CriteriaSuggestions.Should().Equal("equals:", "text<>", "contains:", "notcontains:", "begins:", "ends:", "blank", "nonblank");
    }

    [Fact]
    public void CreateMenuPlan_UsesAbsoluteColumnNameForBlankHeaders()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 3, 3), new TextValue(""));
        sheet.SetCell(new CellAddress(SheetId, 4, 3), new TextValue("West"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 3, 3),
                new CellAddress(SheetId, 4, 4)),
            FilterColumnOffset: 0);

        var menu = CreateMenuPlan(sheet, plan);

        menu.HeaderText.Should().Be(UiText.Format("AutoFilter_ColumnHeader", "C"));
        menu.Entries.Should().Contain(entry => entry.Header == UiText.Format("AutoFilter_ClearFilterFrom", UiText.Format("AutoFilter_ColumnHeader", "C")));
    }

    [Fact]
    public void CreateMenuPlan_DisablesClearFilterUntilRangeHasFilteredRows()
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

        var withoutFilter = CreateMenuPlan(sheet, plan);
        sheet.FilterHiddenRows.Add(3);
        // A real column filter records the rows it owns per-column (FilterCommand / AverageFilterCommand
        // populate Sheet.ColumnFilterOwnedRows / ActiveValueFilterColumns; FilterHiddenRows alone is only
        // the sheet-wide aggregate written by demo/screenshot code). "Clear Filter From <Column>" is
        // enabled off THIS column's own ownership (R87-commands-autofilter-sort-5-2), not the aggregate,
        // so model the filter as owned by the filtered column (col 1) here.
        sheet.ColumnFilterOwnedRows[1] = [3];
        var withFilter = CreateMenuPlan(sheet, plan);

        withoutFilter.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
            .IsEnabled.Should().BeFalse();
        withFilter.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
            .IsEnabled.Should().BeTrue();
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

        var menu = CreateMenuPlan(sheet, plan);

        menu.Entries.Select(entry => entry.Kind).Should().ContainInOrder(
            AutoFilterMenuEntryKind.SortAscending,
            AutoFilterMenuEntryKind.SortDescending,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.ClearFilter,
            AutoFilterMenuEntryKind.FilterFamily,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.Search,
            AutoFilterMenuEntryKind.SelectAll,
            AutoFilterMenuEntryKind.Separator,
            AutoFilterMenuEntryKind.ChecklistItem,
            AutoFilterMenuEntryKind.ChecklistItem);
    }

    [Fact]
    public void CreateMenuPlan_ExposesExcelVisualSectionsForNestedRendering()
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

        var menu = CreateMenuPlan(sheet, plan);

        menu.Sections.Select(section => section.Kind).Should().Equal(
            AutoFilterMenuSectionKind.Sort,
            AutoFilterMenuSectionKind.FilterCommands,
            AutoFilterMenuSectionKind.Search,
            AutoFilterMenuSectionKind.Checklist);
        menu.Sections.Select(section => section.Label).Should().Equal(
            UiText.Get("AutoFilter_SectionSort"),
            UiText.Get("AutoFilter_SectionFilter"),
            UiText.Get("AutoFilter_SectionSearch"),
            UiText.Get("AutoFilter_SectionValues"));
        menu.Sections[0].Entries.Select(entry => entry.Header).Should().Equal(UiText.Get("AutoFilter_SortAToZ"), UiText.Get("AutoFilter_SortZToA"));
        menu.Sections[1].Entries.Select(entry => entry.Header).Should().Equal(
            UiText.Format("AutoFilter_ClearFilterFrom", "Fruit"),
            UiText.Get("AutoFilter_FilterFamily_Text"));
        menu.Sections[2].Entries.Select(entry => entry.Header).Should().Equal(UiText.Get("AutoFilter_Search"), UiText.Get("AutoFilter_SelectAll"));
        menu.Sections[3].Entries.Select(entry => entry.Header).Should().Equal("Apple", "Banana");
    }

    [Theory]
    [InlineData("number", AutoFilterMenuFilterKind.Number, "AutoFilter_SortSmallestToLargest", "AutoFilter_SortLargestToSmallest")]
    [InlineData("date", AutoFilterMenuFilterKind.Date, "AutoFilter_SortOldestToNewest", "AutoFilter_SortNewestToOldest")]
    public void CreateMenuPlan_UsesHostLocalizedExcelSortLabelsForTypedColumns(
        string valueKind,
        AutoFilterMenuFilterKind expectedFilterKind,
        string expectedAscendingKey,
        string expectedDescendingKey)
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Value"));
        if (valueKind == "number")
        {
            sheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(42));
            sheet.SetCell(new CellAddress(SheetId, 3, 1), new NumberValue(7));
        }
        else
        {
            sheet.SetCell(new CellAddress(SheetId, 2, 1), new DateTimeValue(new DateTime(2026, 5, 1).ToOADate()));
            sheet.SetCell(new CellAddress(SheetId, 3, 1), new DateTimeValue(new DateTime(2026, 6, 1).ToOADate()));
        }

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = CreateMenuPlan(sheet, plan);

        menu.FilterKind.Should().Be(expectedFilterKind);
        menu.Sections[0].Entries.Select(entry => entry.Header)
            .Should()
            .Equal(UiText.Get(expectedAscendingKey), UiText.Get(expectedDescendingKey));
    }

    [Fact]
    public void CreateSections_AvoidsRepeatedEntryLinqScans()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation",
            "Filtering",
            "AutoFilterMenuCatalog.cs");
        var createSections = source.Substring(
            source.IndexOf("public static IReadOnlyList<AutoFilterMenuSection> CreateSections", StringComparison.Ordinal),
            source.IndexOf("private static IReadOnlyList<AutoFilterMenuEntry> CreateFilterFamilyChildren", StringComparison.Ordinal)
                - source.IndexOf("public static IReadOnlyList<AutoFilterMenuSection> CreateSections", StringComparison.Ordinal));

        createSections.Should().Contain("foreach (var entry in entries)");
        createSections.Should().NotContain(".Where(");
        createSections.Should().NotContain(".ToList()");
    }
}
