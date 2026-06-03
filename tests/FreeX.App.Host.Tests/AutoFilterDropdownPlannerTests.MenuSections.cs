using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownPlannerTests
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

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

        menu.HeaderText.Should().Be("Fruit");
        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        menu.Entries.Select(entry => entry.Header).Should().ContainInOrder(
            UiText.Get("AutoFilter_SortAscending"),
            UiText.Get("AutoFilter_SortDescending"),
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

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

        menu.HeaderText.Should().Be(UiText.Format("AutoFilter_ColumnHeader", "C"));
        menu.Entries.Should().Contain(entry => entry.Header == UiText.Format("AutoFilter_ClearFilterFrom", UiText.Format("AutoFilter_ColumnHeader", "C")));
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

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

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
        menu.Sections[0].Entries.Select(entry => entry.Header).Should().Equal(UiText.Get("AutoFilter_SortAscending"), UiText.Get("AutoFilter_SortDescending"));
        menu.Sections[1].Entries.Select(entry => entry.Header).Should().Equal(
            UiText.Format("AutoFilter_ClearFilterFrom", "Fruit"),
            UiText.Get("AutoFilter_FilterFamily_Text"));
        menu.Sections[2].Entries.Select(entry => entry.Header).Should().Equal(UiText.Get("AutoFilter_Search"), UiText.Get("AutoFilter_SelectAll"));
        menu.Sections[3].Entries.Select(entry => entry.Header).Should().Equal("Apple", "Banana");
    }

    [Fact]
    public void CreateSections_AvoidsRepeatedEntryLinqScans()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterMenuCatalog.cs"));
        var createSections = source.Substring(
            source.IndexOf("public static IReadOnlyList<AutoFilterMenuSection> CreateSections", StringComparison.Ordinal),
            source.IndexOf("private static IReadOnlyList<AutoFilterMenuEntry> CreateFilterFamilyChildren", StringComparison.Ordinal)
                - source.IndexOf("public static IReadOnlyList<AutoFilterMenuSection> CreateSections", StringComparison.Ordinal));

        createSections.Should().Contain("foreach (var entry in entries)");
        createSections.Should().NotContain(".Where(");
        createSections.Should().NotContain(".ToList()");
    }
}
