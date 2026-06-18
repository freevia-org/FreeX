using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownPlannerTests
{
    [Fact]
    public void CreateMenuPlan_ProvidesNestedFilterFamilySubmenuCommands()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new NumberValue(20));
        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);

        var family = menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily);
        family.Header.Should().Be(UiText.Get("AutoFilter_FilterFamily_Number"));
        family.Children.Select(child => child.Header).Should().ContainInOrder(
            UiText.Get("AutoFilter_Criteria_Equals"),
            UiText.Get("AutoFilter_Criteria_DoesNotEqual"),
            UiText.Get("AutoFilter_Criteria_GreaterThan"),
            UiText.Get("AutoFilter_Criteria_Between"),
            UiText.Get("AutoFilter_Criteria_Top10"),
            UiText.Get("AutoFilter_Criteria_AboveAverage"),
            UiText.Get("AutoFilter_Criteria_Blanks"));
        family.Children.Should().OnlyContain(child => child.Kind == AutoFilterMenuEntryKind.FilterFamilyCommand);
        family.Children.Single(child => child.Header == UiText.Get("AutoFilter_Criteria_GreaterThan")).Value.Should().Be(">");
        family.Children.Single(child => child.Header == UiText.Get("AutoFilter_Criteria_AboveAverage")).Value.Should().Be("above average");
        family.Children.Single(child => child.Header == UiText.Get("AutoFilter_Criteria_Blanks")).Value.Should().Be("blank");
    }

    [Fact]
    public void CreateMenuPlan_ChoosesNumberAndDateFilterFamiliesFromBodyValues()
    {
        var numberSheet = new Sheet(SheetId, "Sheet1");
        numberSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Amount"));
        numberSheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(10));
        numberSheet.SetCell(new CellAddress(SheetId, 3, 1), new NumberValue(20));
        var numberPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var dateSheet = new Sheet(SheetId, "Sheet1");
        dateSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Due"));
        dateSheet.SetCell(new CellAddress(SheetId, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        dateSheet.SetCell(new CellAddress(SheetId, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 21)));
        var datePlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .FilterKind.Should().Be(AutoFilterMenuFilterKind.Number);
        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .Entries.Single(entry => entry.Header == UiText.Get("AutoFilter_FilterFamily_Number"))
            .CriteriaSuggestions.Should().Equal("=", "<>", ">", ">=", "<", "<=", "between:", "top:", "bottom:", "toppercent:", "bottompercent:", "above average", "below average", "blank", "nonblank");

        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .FilterKind.Should().Be(AutoFilterMenuFilterKind.Date);
        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .Entries.Single(entry => entry.Header == UiText.Get("AutoFilter_FilterFamily_Date"))
            .CriteriaSuggestions.Should().Equal("date=", "date<>", "date>", "date>=", "date<", "date<=", "datebetween:", "blank", "nonblank");
    }

    [Fact]
    public void CreateMenuPlan_OffersBlankAndNonblankCriteriaForEveryFilterFamily()
    {
        var textSheet = new Sheet(SheetId, "Sheet1");
        textSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Name"));
        textSheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Quinn"));
        var textPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        var numberSheet = new Sheet(SheetId, "Sheet1");
        numberSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Amount"));
        numberSheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(10));
        var numberPlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        var dateSheet = new Sheet(SheetId, "Sheet1");
        dateSheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Due"));
        dateSheet.SetCell(new CellAddress(SheetId, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        var datePlan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(textSheet, textPlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
        AutoFilterDropdownPlanner.CreateMenuPlan(numberSheet, numberPlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
        AutoFilterDropdownPlanner.CreateMenuPlan(dateSheet, datePlan)
            .Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .CriteriaSuggestions.Should().ContainInOrder("blank", "nonblank");
    }
}
