using System.IO;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AutoFilterDialogTests
{
    [Fact]
    public void FilterItems_ReturnsSearchMatchesWithoutChangingSelection()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var filtered = AutoFilterDialog.FilterItems(items, "app");

        filtered.Should().Equal(new AutoFilterDialogItem("Apple", "Apple", true));
        items.Should().Contain(new AutoFilterDialogItem("Banana", "Banana", false));
    }

    [Fact]
    public void SelectAllAndClearAll_UpdateChecklistSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        AutoFilterDialog.SelectAll(items).Should().OnlyContain(item => item.IsSelected);
        AutoFilterDialog.ClearAll(items).Should().OnlyContain(item => !item.IsSelected);
    }

    [Fact]
    public void DialogItems_AreMutableForChecklistBinding()
    {
        var item = new AutoFilterDialogItem("Apple", "Apple", true);

        item.IsSelected = false;

        AutoFilterDialog.BuildResult(AutoFilterSortDirection.None, [item], "", "")
            .SelectedValues.Should().BeEmpty();
    }

    [Fact]
    public void BuildResult_IncludesSortDirectionChecklistValuesSearchAndCriteriaText()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.Descending,
            items,
            "a",
            "contains: App");

        result.SortDirection.Should().Be(AutoFilterSortDirection.Descending);
        result.SelectedValues.Should().Equal("Apple", "");
        result.SearchText.Should().Be("a");
        result.CriteriaText.Should().Be("contains: App");
        result.ColorFilter.Should().BeNull();
    }

    [Fact]
    public void BuildResult_CarriesOptionalColorFilter()
    {
        var color = new CellColor(33, 115, 70);

        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            color);

        result.ColorFilter.Should().Be(color);
        result.CriteriaText.Should().Be("Apple");
    }

    [Fact]
    public void GetCriteriaSuggestions_ReturnsFilterFamilyCriteriaFromMenuPlan()
    {
        var menuPlan = new AutoFilterMenuPlan(
            "Fruit",
            AutoFilterMenuFilterKind.Text,
            [
                new AutoFilterMenuEntry("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new AutoFilterMenuEntry("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:", "blank"]),
                new AutoFilterMenuEntry(new AutoFilterChecklistItem("Apple", "Apple"))
            ]);

        AutoFilterDialog.GetCriteriaSuggestions(menuPlan)
            .Should()
            .Equal("contains:", "blank");
    }

    [Fact]
    public void DialogSearch_NarrowsChecklistWithoutDroppingHiddenSelections()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("_searchBox.TextChanged");
        source.Should().Contain("FilterItems(_allItems, _searchBox.Text)");
        source.Should().Contain("BuildResult(GetSortDirection(), _allItems");
    }

    [Fact]
    public void DialogControls_ExposeExcelStyleKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        foreach (var content in new[]
        {
            "_No sort",
            "Sort _A to Z",
            "Sort _Z to A",
            "_Select All",
            "_Clear All",
            "_OK",
            "_Cancel"
        })
            source.Should().Contain($"Content = \"{content}\"");

        source.Should().Contain("Content = \"_Criteria text\"");
        source.Should().Contain("Content = \"_Search\"");
    }

    [Fact]
    public void DialogControls_ExposeFilterByColorPickerWhenMenuPlanSupportsIt()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("_filterByColorButton");
        source.Should().Contain("Content = \"Filter by _Color\"");
        source.Should().Contain("new ColorPickerDialog(_selectedColorFilter, allowNoColor: true)");
        source.Should().Contain("HasFilterByColorEntry");
    }
}
