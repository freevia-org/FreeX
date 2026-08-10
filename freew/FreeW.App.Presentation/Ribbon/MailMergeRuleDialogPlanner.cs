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

public readonly record struct MailMergeRuleNameValueDialogResult(string Name, string Value);

/// <summary>
/// Owns the mutable condition/operator state shared by the IF, SKIPIF, and NEXTIF dialog projections.
/// Native hosts remain responsible for controls, focus, modality, and layout.
/// </summary>
public sealed class MailMergeRuleConditionDialogSession
{
    private readonly string[] _fieldNames;

    public MailMergeRuleConditionDialogSession(IEnumerable<string>? fieldNames)
    {
        _fieldNames = fieldNames?.ToArray() ?? [];
        SelectedOperatorIndex = 0;
    }

    public IReadOnlyList<string> FieldNames => _fieldNames;

    public string InitialFieldName => _fieldNames.FirstOrDefault() ?? string.Empty;

    public IReadOnlyList<MailMergeConditionOperatorChoice> ConditionOperators =>
        MailMergeRuleDialogPlanner.GetConditionOperators();

    public int SelectedOperatorIndex { get; private set; }

    public MergeConditionOperator SelectedOperator =>
        MailMergeRuleDialogPlanner.GetConditionOperator(SelectedOperatorIndex);

    public bool IsComparisonValueEnabled =>
        MailMergeRuleDialogPlanner.IsComparisonValueEnabled(SelectedOperator);

    public void SelectOperator(int selectedIndex) =>
        SelectedOperatorIndex = MailMergeRuleDialogPlanner.NormalizeOperatorIndex(selectedIndex);

    public MailMergeRuleIfDialogResult AcceptIf(
        string? fieldName,
        string? value,
        string? trueText,
        string? falseText) =>
        MailMergeRuleDialogPlanner.CreateIfResult(
            fieldName,
            SelectedOperatorIndex,
            value,
            trueText,
            falseText);

    public MailMergeRuleConditionDialogResult AcceptCondition(
        string? fieldName,
        string? value) =>
        MailMergeRuleDialogPlanner.CreateConditionResult(
            fieldName,
            SelectedOperatorIndex,
            value);
}

/// <summary>Owns shared validation and normalization for ASK and SET dialog acceptance.</summary>
public sealed class MailMergeRuleNameValueDialogSession
{
    public bool IsNameValid(string? name) => !string.IsNullOrWhiteSpace(name);

    public MailMergeRuleNameValueDialogResult? Accept(string? name, string? value) =>
        IsNameValid(name)
            ? MailMergeRuleDialogPlanner.CreateNameValueResult(name!.Trim(), value)
            : null;
}

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
        ConditionOperators[NormalizeOperatorIndex(selectedIndex)].Operator;

    public static int NormalizeOperatorIndex(int index) =>
        index >= 0 && index < ConditionOperators.Length ? index : 0;

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

    public static MailMergeRuleNameValueDialogResult CreateNameValueResult(
        string? name,
        string? value) =>
        new(name ?? string.Empty, value ?? string.Empty);

}
