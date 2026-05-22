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
    public void SetSelectionForSearch_UpdatesVisibleMatchesAndPreservesHiddenSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Apricot", "Apricot", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        var updated = AutoFilterDialog.SetSelectionForSearch(items, "ap", isSelected: true);

        updated.Should().Equal(
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Apricot", "Apricot", true),
            new AutoFilterDialogItem("Banana", "Banana", true));
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Text Filters")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Number Filters")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Date Filters")]
    public void GetFilterFamilyHeader_ReturnsExcelTypedFilterAffordance(AutoFilterMenuFilterKind filterKind, string expected)
    {
        AutoFilterDialog.GetFilterFamilyHeader(filterKind).Should().Be(expected);
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

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Contains", "contains:Blue")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Greater Than", ">42")]
    [InlineData(AutoFilterMenuFilterKind.Date, "After", "date>2026-05-21")]
    public void BuildCriteriaText_UsesTypedOperatorTemplates(
        AutoFilterMenuFilterKind filterKind,
        string optionLabel,
        string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(filterKind)
            .Single(item => item.Label == optionLabel);

        var value = filterKind switch
        {
            AutoFilterMenuFilterKind.Text => "Blue",
            AutoFilterMenuFilterKind.Number => "42",
            _ => "2026-05-21"
        };

        AutoFilterDialog.BuildCriteriaText(option, value).Should().Be(expected);
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Blanks", "blank")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Above Average", "above average")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Between", "datebetween:")]
    public void BuildCriteriaText_AllowsValueOptionalTypedCriteria(
        AutoFilterMenuFilterKind filterKind,
        string optionLabel,
        string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(filterKind)
            .Single(item => item.Label == optionLabel);

        AutoFilterDialog.BuildCriteriaText(option, string.Empty).Should().Be(expected);
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
            "_Clear Filter From",
            "_Text Filters",
            "_Number Filters",
            "_Date Filters",
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

    [Fact]
    public void DialogControls_UseTypedCriteriaControlsInsteadOfFocusOnlyFilterButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("_criteriaOperatorBox");
        source.Should().Contain("_criteriaValueBox");
        source.Should().Contain("_criteriaConnectorBox");
        source.Should().Contain("_criteriaOperatorBox2");
        source.Should().Contain("_criteriaValueBox2");
        source.Should().Contain("_customFilterGroup");
        source.Should().Contain("Header = \"Custom filter\"");
        source.Should().Contain("IsReadOnly = true");
        source.Should().Contain("_customFilterGroup.Visibility = Visibility.Visible");
        source.Should().Contain("_criteriaSuggestionLabel.Visibility = Visibility.Visible");
        source.Should().Contain("BuildCriteriaText");
        source.Should().Contain("BuildCompositeCriteriaText");
        source.Should().NotContain("filterButton.Click += (_, _) => _criteriaBox.Focus()");
    }

    [Fact]
    public void DialogLayout_UsesSeparatorsBetweenExcelFilterMenuSections()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("AddFilterMenuSeparator(stack)");
        source.Should().Contain("new Separator");
    }

    [Theory]
    [InlineData("And", ">10", "<20", "and:>10|<20")]
    [InlineData("Or", "begins:Red", "ends:Apple", "or:begins:Red|ends:Apple")]
    public void BuildCompositeCriteriaText_ComposesExcelCustomFilterRows(
        string connector,
        string firstCriteria,
        string secondCriteria,
        string expected)
    {
        AutoFilterDialog.BuildCompositeCriteriaText(firstCriteria, connector, secondCriteria)
            .Should()
            .Be(expected);
    }
}
