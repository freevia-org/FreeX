using System.Linq;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the Avalonia adapter over the shared AutoFilter menu plan. Shared menu shape, value
/// extraction, active-filter detection, and ordering live in presentation tests.
/// </summary>
public sealed class AutoFilterMenuPlannerTests
{
    [Fact]
    public void Build_FromSharedPlan_PreservesEntryKindsLabelsValuesAndEnablement()
    {
        var plan = new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [
                new("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new("Clear Filter from Region", AutoFilterMenuEntryKind.ClearFilter, isEnabled: false),
                new("Search", AutoFilterMenuEntryKind.Search),
                new("Select All", AutoFilterMenuEntryKind.SelectAll, isChecked: null),
                new("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:", "blank"], "Text Filters"),
                new(new AutoFilterChecklistItem("West", "west", IsChecked: false))
            ],
            ColorOptions:
            [
                new("#217346", AutoFilterColorFilterKind.CellFillColor, new CellColor(0x21, 0x73, 0x46))
            ]);

        var model = AutoFilterMenuPlanner.Build(plan);

        model.Header.Should().Be("Region");
        model.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        model.Items.Select(item => item.Kind).Should().Equal(
            AutoFilterMenuItemKind.SortAscending,
            AutoFilterMenuItemKind.ClearFilter,
            AutoFilterMenuItemKind.Search,
            AutoFilterMenuItemKind.SelectAll,
            AutoFilterMenuItemKind.FilterFamily,
            AutoFilterMenuItemKind.ChecklistItem);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeFalse();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem).Value.Should().Be("west");
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.SelectAll).IsChecked.Should().BeNull();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem).IsChecked.Should().BeFalse();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.SortAscending)
            .IconKind.Should().Be(RibbonCommandIconKind.SortAscending);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.Search)
            .FocusRole.Should().Be(AutoFilterMenuEntryFocusRole.SearchBox);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.SelectAll)
            .FocusRole.Should().Be(AutoFilterMenuEntryFocusRole.TriStateSelectAll);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem)
            .ParticipatesInSearch.Should().BeTrue();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)
            .ShowsContinuation.Should().BeTrue();
        model.CriteriaSuggestions.Should().Equal("contains:", "blank");
        model.CriteriaOptions.Should().Contain(option => option.CriteriaPrefix == "contains:");
        model.ColorOptions.Should().ContainSingle(option => option.Kind == AutoFilterColorFilterKind.CellFillColor);
    }

    [Fact]
    public void CreateDialogItems_ProjectsChecklistRowsForSearchAndSelection()
    {
        var plan = new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [
                new(new AutoFilterChecklistItem("North", "north", IsChecked: true)),
                new(new AutoFilterChecklistItem("West", "west", IsChecked: false))
            ]);
        var model = AutoFilterMenuPlanner.Build(plan);

        var items = AutoFilterMenuPlanner.CreateDialogItems(model);
        var filtered = AutoFilterMenuPlanner.FilterItems(items, "we");
        var updated = AutoFilterMenuPlanner.SetSelectionForSearch(items, "we", isSelected: true);

        items.Should().BeEquivalentTo(
        [
            new AutoFilterDialogItem("North", "north", true),
            new AutoFilterDialogItem("West", "west", false)
        ]);
        filtered.Select(item => item.Value).Should().Equal("west");
        updated.Single(item => item.Value == "west").IsSelected.Should().BeTrue();
        AutoFilterMenuPlanner.SelectAllState(updated).Should().BeTrue();
    }

    [Fact]
    public void Build_FromSharedPlan_PreservesExcelPopupKeyboardAndSelectionRoles()
    {
        var plan = new AutoFilterMenuPlan(
            "Status",
            AutoFilterMenuFilterKind.Text,
            [
                new("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:"], "Text Filters"),
                new("Search", AutoFilterMenuEntryKind.Search),
                new("Select All", AutoFilterMenuEntryKind.SelectAll, isChecked: null),
                new(new AutoFilterChecklistItem("Open", "Open", IsChecked: true)),
                new(new AutoFilterChecklistItem("Closed", "Closed", IsChecked: false))
            ]);

        var model = AutoFilterMenuPlanner.Build(plan);

        model.Items.Select(item => (item.Kind, item.FocusRole)).Should().ContainInOrder(
            (AutoFilterMenuItemKind.SortAscending, AutoFilterMenuEntryFocusRole.Command),
            (AutoFilterMenuItemKind.FilterFamily, AutoFilterMenuEntryFocusRole.Submenu),
            (AutoFilterMenuItemKind.Search, AutoFilterMenuEntryFocusRole.SearchBox),
            (AutoFilterMenuItemKind.SelectAll, AutoFilterMenuEntryFocusRole.TriStateSelectAll),
            (AutoFilterMenuItemKind.ChecklistItem, AutoFilterMenuEntryFocusRole.ChecklistItem),
            (AutoFilterMenuItemKind.ChecklistItem, AutoFilterMenuEntryFocusRole.ChecklistItem));
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)
            .IconKind.Should().Be(RibbonCommandIconKind.Filter);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)
            .ShowsContinuation.Should().BeTrue();
        model.Items.Where(item => item.ParticipatesInSearch)
            .Select(item => item.Kind)
            .Should()
            .Equal(
                AutoFilterMenuItemKind.Search,
                AutoFilterMenuItemKind.SelectAll,
                AutoFilterMenuItemKind.ChecklistItem,
                AutoFilterMenuItemKind.ChecklistItem);
    }

    [Theory]
    [InlineData("number", AutoFilterMenuFilterKind.Number, "Sort Smallest to Largest", "Sort Largest to Smallest")]
    [InlineData("date", AutoFilterMenuFilterKind.Date, "Sort Oldest to Newest", "Sort Newest to Oldest")]
    public void Build_FromSharedTypedColumnPlan_UsesExcelSortLabels(
        string valueKind,
        AutoFilterMenuFilterKind expectedFilterKind,
        string expectedAscending,
        string expectedDescending)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        if (valueKind == "number")
        {
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        }
        else
        {
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(new DateTime(2026, 5, 1).ToOADate()));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new DateTimeValue(new DateTime(2026, 6, 1).ToOADate()));
        }

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);
        var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook,
            sheet,
            plan,
            InvariantAutoFilterMenuTextProvider.Instance,
            InvariantAutoFilterMenuTextProvider.BlankDisplayText);

        var model = AutoFilterMenuPlanner.Build(menuPlan);

        model.FilterKind.Should().Be(expectedFilterKind);
        model.Items.Where(item => item.Kind is AutoFilterMenuItemKind.SortAscending or AutoFilterMenuItemKind.SortDescending)
            .Select(item => item.Label)
            .Should()
            .Equal(expectedAscending, expectedDescending);
    }

    [Fact]
    public void BuildResult_UsesSharedSearchAndCriteriaSemantics()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("North", "north", true),
            new AutoFilterDialogItem("West", "west", true),
        };
        var option = AutoFilterMenuPlanner.Build(new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [])).CriteriaOptions.Single(criteria => criteria.CriteriaPrefix == "contains:");

        var result = AutoFilterMenuPlanner.BuildResult(
            items,
            searchText: "we",
            AutoFilterMenuPlanner.BuildCriteriaText(option, "st"));

        result.SearchText.Should().Be("we");
        result.SelectedValues.Should().Equal("west");
        result.CriteriaText.Should().Be("contains:st");
    }

    [Fact]
    public void BuildCompletedCriteriaText_LeavesUntouchedValueCriteriaEmpty()
    {
        var equals = new AutoFilterCriteriaOption("Equals", "text=", RequiresValue: true);
        var blanks = new AutoFilterCriteriaOption("Blanks", "blank", RequiresValue: false);
        var between = new AutoFilterCriteriaOption("Between", "between:", RequiresValue: true);

        AutoFilterMenuPlanner.BuildCompletedCriteriaText(equals, null).Should().BeEmpty();
        AutoFilterMenuPlanner.BuildCompletedCriteriaText(equals, "North").Should().Be("text=North");
        AutoFilterMenuPlanner.BuildCompletedCriteriaText(blanks, null).Should().Be("blank");
        AutoFilterMenuPlanner.BuildCompletedCriteriaText(between, "10", null).Should().BeEmpty();
        AutoFilterMenuPlanner.BuildCompletedCriteriaText(between, "10", "20").Should().Be("between:10:20");
    }

    [Fact]
    public void FlyoutCommandRows_RenderSharedIconColumnFromMenuPlan()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs"));

        source.Should().Contain("using Free.Shared.Ribbon.Avalonia;");
        source.Should().Contain("CreateAutoFilterActionButton(AutoFilterMenuItem item, Action onClick, bool isEnabled = true)");
        source.Should().Contain("AvaloniaRibbonIcons.BuildMonochrome(item.IconKind, 14, null, Brush(0x21, 0x21, 0x21))");
        source.Should().Contain("Text = item.Label");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
