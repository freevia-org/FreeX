using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeRuleDialogPlannerTests
{
    [Fact]
    public void ConditionOperators_AreExposedInWordDialogOrder()
    {
        var choices = MailMergeRuleDialogPlanner.GetConditionOperators();

        choices.Select(choice => choice.Operator).Should().Equal(
            MergeConditionOperator.Equal,
            MergeConditionOperator.NotEqual,
            MergeConditionOperator.LessThan,
            MergeConditionOperator.LessThanOrEqual,
            MergeConditionOperator.GreaterThan,
            MergeConditionOperator.GreaterThanOrEqual,
            MergeConditionOperator.IsBlank,
            MergeConditionOperator.IsNotBlank,
            MergeConditionOperator.Contains);
        choices.Select(choice => choice.Label).Should().ContainInOrder(
            "Equal to (=)",
            "Not equal to (<>)",
            "Less than (<)",
            "Less than or equal (<=)",
            "Greater than (>)",
            "Greater than or equal (>=)",
            "Is blank",
            "Is not blank",
            "Contains");
    }

    [Theory]
    [InlineData((int)MergeConditionOperator.Equal, true)]
    [InlineData((int)MergeConditionOperator.Contains, true)]
    [InlineData((int)MergeConditionOperator.IsBlank, false)]
    [InlineData((int)MergeConditionOperator.IsNotBlank, false)]
    public void IsComparisonValueEnabled_DisablesBlankOperators(int operatorValue, bool expected)
    {
        MailMergeRuleDialogPlanner.IsComparisonValueEnabled((MergeConditionOperator)operatorValue)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, MergeConditionOperator.Equal)]
    [InlineData(0, MergeConditionOperator.Equal)]
    [InlineData(6, MergeConditionOperator.IsBlank)]
    [InlineData(99, MergeConditionOperator.Equal)]
    public void GetConditionOperator_DefaultsOutOfRangeToEqual(int index, MergeConditionOperator expected)
    {
        MailMergeRuleDialogPlanner.GetConditionOperator(index).Should().Be(expected);
    }

    [Fact]
    public void CreateIfResult_NormalizesNullTextAndUsesSelectedOperator()
    {
        var result = MailMergeRuleDialogPlanner.CreateIfResult(
            fieldName: null,
            selectedOperatorIndex: 8,
            value: null,
            trueText: "yes",
            falseText: null);

        result.FieldName.Should().BeEmpty();
        result.Operator.Should().Be(MergeConditionOperator.Contains);
        result.Value.Should().BeEmpty();
        result.TrueText.Should().Be("yes");
        result.FalseText.Should().BeEmpty();
    }

    [Fact]
    public void CreateConditionResult_UsesSelectedOperator()
    {
        var result = MailMergeRuleDialogPlanner.CreateConditionResult("City", 1, "Paris");

        result.FieldName.Should().Be("City");
        result.Operator.Should().Be(MergeConditionOperator.NotEqual);
        result.Value.Should().Be("Paris");
    }

    [Fact]
    public void CreateNameValueResult_NormalizesNullText()
    {
        var result = MailMergeRuleDialogPlanner.CreateNameValueResult(null, "CustomerName");

        result.Name.Should().BeEmpty();
        result.Value.Should().Be("CustomerName");
    }
}
