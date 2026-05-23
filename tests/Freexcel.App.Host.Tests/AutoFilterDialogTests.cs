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
            new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));
        result.CriteriaText.Should().Be("Apple");
    }

    [Fact]
    public void BuildResult_DistinguishesNoFillColorFilterFromNoColorSelection()
    {
        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));
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

    [Fact]
    public void BuildBetweenCriteriaText_UsesSeparateMinimumAndMaximumValues()
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == "Between");

        AutoFilterDialog.BuildBetweenCriteriaText(option, " 10 ", "20")
            .Should()
            .Be("between:10:20");
    }

    [Theory]
    [InlineData("Top 10", "top:5")]
    [InlineData("Bottom 10 Percent", "bottompercent:25")]
    public void BuildTopBottomCriteriaText_UsesExcelCountControl(string optionLabel, string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == optionLabel);

        AutoFilterDialog.BuildTopBottomCriteriaText(option, expected.Split(':')[1])
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Today", "date=2026-05-22")]
    [InlineData("Yesterday", "date=2026-05-21")]
    [InlineData("Tomorrow", "date=2026-05-23")]
    [InlineData("This Week", "datebetween:2026-05-17:2026-05-23")]
    [InlineData("Last Week", "datebetween:2026-05-10:2026-05-16")]
    [InlineData("Next Week", "datebetween:2026-05-24:2026-05-30")]
    [InlineData("This Month", "datebetween:2026-05-01:2026-05-31")]
    [InlineData("Last Month", "datebetween:2026-04-01:2026-04-30")]
    [InlineData("Next Month", "datebetween:2026-06-01:2026-06-30")]
    [InlineData("This Year", "datebetween:2026-01-01:2026-12-31")]
    [InlineData("Last Year", "datebetween:2025-01-01:2025-12-31")]
    [InlineData("Next Year", "datebetween:2027-01-01:2027-12-31")]
    public void BuildDatePresetCriteriaText_UsesExcelDateFilterPresets(string preset, string expected)
    {
        AutoFilterDialog.BuildDatePresetCriteriaText(preset, new DateTime(2026, 5, 22))
            .Should()
            .Be(expected);
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

        source.Should().Contain("_filterByColorGroup");
        source.Should().Contain("Header = \"Filter by Color\"");
        source.Should().Contain("PopulateColorChoices");
        source.Should().Contain("Cell Color");
        source.Should().Contain("Font Color");
        source.Should().Contain("CreateColorSwatch");
        source.Should().NotContain("new ColorPickerDialog(_selectedColorFilter, allowNoColor: true)");
        source.Should().Contain("HasFilterByColorEntry");
    }

    [Fact]
    public void DataFilterCommands_RouteColorFiltersAndCompositeCriteriaToRealCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("result.ColorFilter is { } colorFilter");
        source.Should().Contain("new CellFillColorFilterCommand");
        source.Should().Contain("new CellNoFillColorFilterCommand");
        source.Should().Contain("new CellFontColorFilterCommand");
        source.Should().Contain("filterText.StartsWith(\"and:\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("filterText.StartsWith(\"or:\", StringComparison.OrdinalIgnoreCase)");
    }

    [Fact]
    public void DialogControls_UseTypedCriteriaControlsInsteadOfFocusOnlyFilterButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("_criteriaOperatorBox");
        source.Should().Contain("_criteriaValueBox");
        source.Should().Contain("_betweenCriteriaPanel");
        source.Should().Contain("_betweenMinBox");
        source.Should().Contain("_betweenMaxBox");
        source.Should().Contain("_topBottomCriteriaPanel");
        source.Should().Contain("_topBottomCountBox");
        source.Should().Contain("_datePresetBox");
        source.Should().Contain("\"This Week\"");
        source.Should().Contain("\"Last Week\"");
        source.Should().Contain("\"Next Week\"");
        source.Should().Contain("\"This Year\"");
        source.Should().Contain("\"Last Year\"");
        source.Should().Contain("\"Next Year\"");
        source.Should().Contain("Date _preset");
        source.Should().Contain("_criteriaConnectorBox");
        source.Should().Contain("_criteriaOperatorBox2");
        source.Should().Contain("_criteriaValueBox2");
        source.Should().Contain("_customFilterGroup");
        source.Should().Contain("Header = \"Custom filter\"");
        source.Should().Contain("IsReadOnly = true");
        source.Should().Contain("_customFilterGroup.Visibility = Visibility.Visible");
        source.Should().Contain("_criteriaSuggestionLabel.Visibility = Visibility.Visible");
        source.Should().Contain("BuildCriteriaText");
        source.Should().Contain("BuildBetweenCriteriaText");
        source.Should().Contain("BuildTopBottomCriteriaText");
        source.Should().Contain("BuildDatePresetCriteriaText");
        source.Should().Contain("BuildCompositeCriteriaText");
        source.Should().Contain("RefreshSpecialCriteriaPanels");
        source.Should().Contain("SelectedDatePresetCriteria");
        source.Should().Contain("!string.IsNullOrWhiteSpace(_criteriaValueBox2.Text)");
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
