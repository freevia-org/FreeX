using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "AutoFilter_FilterFamily_Text")]
    [InlineData(AutoFilterMenuFilterKind.Number, "AutoFilter_FilterFamily_Number")]
    [InlineData(AutoFilterMenuFilterKind.Date, "AutoFilter_FilterFamily_Date")]
    public void GetFilterFamilyHeader_ReturnsExcelTypedFilterAffordance(AutoFilterMenuFilterKind filterKind, string expectedKey)
    {
        AutoFilterDialog.GetFilterFamilyHeader(filterKind).Should().Be(UiText.Get(expectedKey));
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Sort _A to Z", "Sort _Z to A")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Sort _Smallest to Largest", "Sort _Largest to Smallest")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Sort _Oldest to Newest", "Sort _Newest to Oldest")]
    public void GetSortLabels_ReturnsExcelLabelsForDetectedFilterValueType(
        AutoFilterMenuFilterKind filterKind,
        string expectedAscending,
        string expectedDescending)
    {
        AutoFilterDialog.GetSortLabels(filterKind)
            .Should()
            .Be((expectedAscending, expectedDescending));
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
    [InlineData(AutoFilterMenuFilterKind.Text, "AutoFilter_Criteria_Contains", "contains:Blue")]
    [InlineData(AutoFilterMenuFilterKind.Number, "AutoFilter_Criteria_GreaterThan", ">42")]
    [InlineData(AutoFilterMenuFilterKind.Date, "AutoFilter_Criteria_After", "date>2026-05-21")]
    public void BuildCriteriaText_UsesTypedOperatorTemplates(
        AutoFilterMenuFilterKind filterKind,
        string optionLabelKey,
        string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(filterKind)
            .Single(item => item.Label == UiText.Get(optionLabelKey));

        var value = filterKind switch
        {
            AutoFilterMenuFilterKind.Text => "Blue",
            AutoFilterMenuFilterKind.Number => "42",
            _ => "2026-05-21"
        };

        AutoFilterDialog.BuildCriteriaText(option, value).Should().Be(expected);
    }

    [Fact]
    public void CriteriaLabels_UseSharedPresentationCriteriaCatalog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AutoFilterCriteriaLabels.cs");

        source.Should().Contain("AutoFilterMenuCatalog.GetFilterFamilyDescriptor");
        source.Should().Contain("AutoFilterMenuCatalog.GetCriteriaDescriptors");
        source.Should().NotContain("\"AutoFilter_Criteria_");
        source.Should().NotContain("switch");
    }

    [Fact]
    public void BuildBetweenCriteriaText_UsesSeparateMinimumAndMaximumValues()
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == UiText.Get("AutoFilter_Criteria_Between"));

        AutoFilterDialog.BuildBetweenCriteriaText(option, " 10 ", "20")
            .Should()
            .Be("between:10:20");
    }

    [Theory]
    [InlineData("AutoFilter_Criteria_Top10", "top:5")]
    [InlineData("AutoFilter_Criteria_Bottom10Percent", "bottompercent:25")]
    public void BuildTopBottomCriteriaText_UsesExcelCountControl(string optionLabelKey, string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == UiText.Get(optionLabelKey));

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
    public void CriteriaPartial_DelegatesPureCriteriaBehaviorToPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AutoFilterDialog.Criteria.cs");

        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildResult");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildCriteriaText");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildCompositeCriteriaText");
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Number)]
    [InlineData(AutoFilterMenuFilterKind.Date)]
    public void R107_GetSecondRowCriteriaOptions_ExcludesUncombinableSpecialCriteria(AutoFilterMenuFilterKind filterKind)
    {
        var criteriaOptions = AutoFilterDialog.GetCriteriaOptions(filterKind);

        var secondRowOptions = AutoFilterDialog.GetSecondRowCriteriaOptions(criteriaOptions);

        secondRowOptions.Should().NotContain(option => AutoFilterDialogCriteriaPlanner.IsBetweenOption(option));
        secondRowOptions.Should().NotContain(option => AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option));
        secondRowOptions.Should().NotContain(option => AutoFilterDialogCriteriaPlanner.IsAverageOption(option));
        secondRowOptions.Count.Should().BeLessThan(criteriaOptions.Count);

        // Every excluded option must still be a real row-1 option -- this is a filter, not a typo that
        // happens to drop unrelated entries too.
        foreach (var option in criteriaOptions)
        {
            if (!secondRowOptions.Contains(option))
            {
                (AutoFilterDialogCriteriaPlanner.IsBetweenOption(option) ||
                    AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option) ||
                    AutoFilterDialogCriteriaPlanner.IsAverageOption(option))
                    .Should()
                    .BeTrue($"'{option.CriteriaPrefix}' was dropped from row 2 for no recognized reason");
            }
        }
    }

    [Fact]
    public void R107_GetSecondRowCriteriaOptions_TextFamilyHasNoSpecialCriteriaToExclude()
    {
        var criteriaOptions = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Text);

        AutoFilterDialog.GetSecondRowCriteriaOptions(criteriaOptions)
            .Should()
            .Equal(criteriaOptions);
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

    [Fact]
    public void TypedCriteriaResult_DrivesFilterConditionCommandRowVisibility()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == UiText.Get("AutoFilter_Criteria_GreaterThan"));
        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [
                new AutoFilterDialogItem("5", "5", true),
                new AutoFilterDialogItem("10", "10", true)
            ],
            "",
            AutoFilterDialog.BuildCriteriaText(option, "7"));

        FilterInputParser.TryParseCriterion(result.CriteriaText, out var criterion, out var error)
            .Should()
            .BeTrue(error);
        new FilterConditionCommand(sheet.Id, range, 1, criterion!).Apply(new TestCommandContext(workbook))
            .Success
            .Should()
            .BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2);
        sheet.FilterHiddenRows.Should().NotContain(3);
    }

    [Fact]
    public void CriteriaPlanner_ChecklistHotPathsAvoidLinqMaterialization()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Presentation", "Filtering", "AutoFilterDialogCriteriaPlanner.cs");
        var checklistBlock = source[
            source.IndexOf("public static IReadOnlyList<AutoFilterDialogItem> FilterItems", StringComparison.Ordinal)..
            source.IndexOf("public static IReadOnlyList<AutoFilterDialogItem> SelectAll", StringComparison.Ordinal)];
        var resultBlock = source[
            source.IndexOf("public static AutoFilterDialogResult BuildResult", StringComparison.Ordinal)..
            source.IndexOf("public static AutoFilterDialogResult CreateClearFilterResult", StringComparison.Ordinal)];
        var suggestionsBlock = source[
            source.IndexOf("public static IReadOnlyList<string> GetCriteriaSuggestions", StringComparison.Ordinal)..
            source.IndexOf("public static string BuildCriteriaText", StringComparison.Ordinal)];

        checklistBlock.Should().Contain("foreach (var item in items)");
        checklistBlock.Should().NotContain(".Where(");
        checklistBlock.Should().NotContain(".Select(");
        checklistBlock.Should().NotContain(".ToList(");
        checklistBlock.Should().NotContain(".ToHashSet(");
        resultBlock.Should().Contain("foreach (var item in resultItems)");
        resultBlock.Should().NotContain(".Where(");
        resultBlock.Should().NotContain(".Select(");
        resultBlock.Should().NotContain(".ToList(");
        suggestionsBlock.Should().Contain("foreach (var entry in menuPlan.Entries)");
        suggestionsBlock.Should().NotContain(".FirstOrDefault(");
        suggestionsBlock.Should().NotContain(".Where(");
        suggestionsBlock.Should().NotContain(".ToList(");
    }
}
