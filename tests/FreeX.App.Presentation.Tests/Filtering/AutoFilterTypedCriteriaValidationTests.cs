using FluentAssertions;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterTypedCriteriaValidationTests
{
    private static AutoFilterCriteriaOption Option(AutoFilterMenuFilterKind kind, Func<AutoFilterCriteriaOption, bool> pick) =>
        AutoFilterMenuPlanner.CreateCriteriaOptions(kind, InvariantAutoFilterMenuTextProvider.Instance).First(pick);

    [Fact]
    public void Between_RequiresBothValues_InWpfOrder()
    {
        var between = Option(AutoFilterMenuFilterKind.Number, AutoFilterDialogCriteriaPlanner.IsBetweenOption);

        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(between, null, "", "5", null)
            .Should().Be(new AutoFilterTypedCriteriaIssue("AutoFilter_EnterFirstBetweenValue", AutoFilterTypedCriteriaField.Minimum));
        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(between, null, "1", " ", null)
            .Should().Be(new AutoFilterTypedCriteriaIssue("AutoFilter_EnterSecondBetweenValue", AutoFilterTypedCriteriaField.Maximum));
        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(between, null, "1", "5", null).Should().BeNull();
    }

    [Fact]
    public void TopBottom_RequiresValidCount()
    {
        var top = Option(AutoFilterMenuFilterKind.Number, AutoFilterDialogCriteriaPlanner.IsTopBottomOption);

        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(top, null, null, null, "")
            .Should().Be(new AutoFilterTypedCriteriaIssue("AutoFilter_EnterValidTopOrBottomCount", AutoFilterTypedCriteriaField.Count));
        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(top, null, null, null, "10").Should().BeNull();
    }

    [Fact]
    public void ValueOperators_RequireAValueUnlessTheOperatorTakesNone()
    {
        var withValue = Option(AutoFilterMenuFilterKind.Text, option => option.RequiresValue);
        var withoutValue = Option(AutoFilterMenuFilterKind.Number, option =>
            !option.RequiresValue && !AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option));

        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(withValue, " ", null, null, null)
            .Should().Be(new AutoFilterTypedCriteriaIssue("AutoFilter_EnterFilterValue", AutoFilterTypedCriteriaField.Value));
        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(withValue, "a*", null, null, null).Should().BeNull();
        AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(withoutValue, null, null, null, null).Should().BeNull();
    }
}
