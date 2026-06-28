using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public readonly record struct MailMergeConditionOperatorChoice(MergeConditionOperator Operator, string Label);

public sealed record MailMergeRuleIfDialogResult(
    string FieldName,
    MergeConditionOperator Operator,
    string Value,
    string TrueText,
    string FalseText);

public sealed record MailMergeRuleConditionDialogResult(
    string FieldName,
    MergeConditionOperator Operator,
    string Value);

public static class MailMergeRuleDialogPlanner
{
    private static readonly MailMergeConditionOperatorChoice[] ConditionOperators =
    [
        new(MergeConditionOperator.Equal, "Equal to (=)"),
        new(MergeConditionOperator.NotEqual, "Not equal to (<>)"),
        new(MergeConditionOperator.LessThan, "Less than (<)"),
        new(MergeConditionOperator.LessThanOrEqual, "Less than or equal (<=)"),
        new(MergeConditionOperator.GreaterThan, "Greater than (>)"),
        new(MergeConditionOperator.GreaterThanOrEqual, "Greater than or equal (>=)"),
        new(MergeConditionOperator.IsBlank, "Is blank"),
        new(MergeConditionOperator.IsNotBlank, "Is not blank"),
        new(MergeConditionOperator.Contains, "Contains"),
    ];

    public static IReadOnlyList<MailMergeConditionOperatorChoice> GetConditionOperators() =>
        ConditionOperators;

    public static MergeConditionOperator GetConditionOperator(int selectedIndex) =>
        ConditionOperators[NormalizeIndex(selectedIndex)].Operator;

    public static bool IsComparisonValueEnabled(MergeConditionOperator op) =>
        op is not MergeConditionOperator.IsBlank and not MergeConditionOperator.IsNotBlank;

    public static MailMergeRuleIfDialogResult CreateIfResult(
        string? fieldName,
        int selectedOperatorIndex,
        string? value,
        string? trueText,
        string? falseText) =>
        new(
            fieldName ?? string.Empty,
            GetConditionOperator(selectedOperatorIndex),
            value ?? string.Empty,
            trueText ?? string.Empty,
            falseText ?? string.Empty);

    public static MailMergeRuleConditionDialogResult CreateConditionResult(
        string? fieldName,
        int selectedOperatorIndex,
        string? value) =>
        new(
            fieldName ?? string.Empty,
            GetConditionOperator(selectedOperatorIndex),
            value ?? string.Empty);

    private static int NormalizeIndex(int index) =>
        index >= 0 && index < ConditionOperators.Length ? index : 0;
}
