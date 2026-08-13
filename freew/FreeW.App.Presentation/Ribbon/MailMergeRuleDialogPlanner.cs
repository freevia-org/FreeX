using Free.Shared.AppServices;
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

public enum MailMergeRuleKind
{
    IfThenElse,
    SkipRecordIf,
    NextRecordIf,
    FillIn,
    Ask,
    Set,
    Ref,
}

public abstract record MailMergeRuleDialogRequest(
    MailMergeRuleKind Kind,
    string Title);

public sealed record MailMergeRuleIfDialogRequest(
    IReadOnlyList<string> FieldNames,
    string Title,
    string FieldNameLabel,
    string ComparisonLabel,
    string CompareToLabel,
    string TrueTextLabel,
    string FalseTextLabel)
    : MailMergeRuleDialogRequest(MailMergeRuleKind.IfThenElse, Title);

public sealed record MailMergeRuleConditionDialogRequest(
    MailMergeRuleKind Kind,
    IReadOnlyList<string> FieldNames,
    string Title,
    string FieldNameLabel,
    string ComparisonLabel,
    string CompareToLabel)
    : MailMergeRuleDialogRequest(Kind, Title);

public sealed record MailMergeRulePromptDialogRequest(
    MailMergeRuleKind Kind,
    string Title,
    string Prompt)
    : MailMergeRuleDialogRequest(Kind, Title);

public sealed record MailMergeRuleNameValueDialogRequest(
    MailMergeRuleKind Kind,
    string Title,
    string NameLabel,
    string ValueLabel)
    : MailMergeRuleDialogRequest(Kind, Title);

public abstract record MailMergeRuleDialogResponse;

public sealed record MailMergeRuleIfDialogResponse(MailMergeRuleIfDialogResult Result)
    : MailMergeRuleDialogResponse;

public sealed record MailMergeRuleConditionDialogResponse(MailMergeRuleConditionDialogResult Result)
    : MailMergeRuleDialogResponse;

public sealed record MailMergeRulePromptDialogResponse(string Value)
    : MailMergeRuleDialogResponse;

public sealed record MailMergeRuleNameValueDialogResponse(MailMergeRuleNameValueDialogResult Result)
    : MailMergeRuleDialogResponse;

public sealed record MailMergeRuleAuthoringExecution(
    bool WasAccepted,
    MailMergeFieldInsertionPlan? InsertionPlan)
{
    public static MailMergeRuleAuthoringExecution Cancelled { get; } = new(false, null);
}

public delegate ValueTask<MailMergeRuleDialogResponse?> MailMergeRuleDialogPresenter(
    MailMergeRuleDialogRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask MailMergeRuleInsertionPresenter(
    MailMergeFieldInsertionPlan plan,
    CancellationToken cancellationToken);

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

    private static readonly IReadOnlyDictionary<MailMergeRuleKind, ResourceTextDescriptor> Titles =
        new Dictionary<MailMergeRuleKind, ResourceTextDescriptor>
        {
            [MailMergeRuleKind.IfThenElse] = Text("MailMerge_Rule_If_Title", MailMergeDialogMetadata.IfThenElseTitle),
            [MailMergeRuleKind.SkipRecordIf] = Text("MailMerge_Rule_SkipRecordIf_Title", "Skip Record If"),
            [MailMergeRuleKind.NextRecordIf] = Text("MailMerge_Rule_NextRecordIf_Title", "Next Record If"),
            [MailMergeRuleKind.FillIn] = Text("MailMerge_Rule_FillIn_Title", "Fill-in"),
            [MailMergeRuleKind.Ask] = Text("MailMerge_Rule_Ask_Title", "Ask"),
            [MailMergeRuleKind.Set] = Text("MailMerge_Rule_Set_Title", "Set Bookmark"),
            [MailMergeRuleKind.Ref] = Text("MailMerge_Rule_Ref_Title", "Ref Bookmark"),
        };

    private static readonly ResourceTextDescriptor FieldNameLabel =
        Text("MailMerge_Rule_FieldName_Label", MailMergeDialogMetadata.FieldNameLabel);
    private static readonly ResourceTextDescriptor ComparisonLabel =
        Text("MailMerge_Rule_Comparison_Label", MailMergeDialogMetadata.ComparisonLabel);
    private static readonly ResourceTextDescriptor CompareToLabel =
        Text("MailMerge_Rule_CompareTo_Label", MailMergeDialogMetadata.CompareToLabel);
    private static readonly ResourceTextDescriptor TrueTextLabel =
        Text("MailMerge_Rule_TrueText_Label", MailMergeDialogMetadata.ThenInsertLabel);
    private static readonly ResourceTextDescriptor FalseTextLabel =
        Text("MailMerge_Rule_FalseText_Label", MailMergeDialogMetadata.OtherwiseInsertLabel);
    private static readonly ResourceTextDescriptor BookmarkNameLabel =
        Text("MailMerge_Rule_BookmarkName_Label", MailMergeDialogMetadata.BookmarkNameLabel);
    private static readonly ResourceTextDescriptor FillInPrompt =
        Text("MailMerge_Rule_FillIn_Prompt", "Enter the prompt text for this Fill-in field:");
    private static readonly ResourceTextDescriptor AskPrompt =
        Text("MailMerge_Rule_Ask_Prompt", "Prompt text:");
    private static readonly ResourceTextDescriptor SetValue =
        Text("MailMerge_Rule_Set_ValueLabel", "Value:");
    private static readonly ResourceTextDescriptor RefPrompt =
        Text("MailMerge_Rule_Ref_Prompt", "Enter the bookmark name to reference:");

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Titles.Values
            .Concat([
                FieldNameLabel,
                ComparisonLabel,
                CompareToLabel,
                TrueTextLabel,
                FalseTextLabel,
                BookmarkNameLabel,
                FillInPrompt,
                AskPrompt,
                SetValue,
                RefPrompt,
            ])
            .Select(text => text.ResourceKey)
            .ToArray();

    public static string ResolveInteractivePromptTitle(
        MailMergeInteractivePromptKind kind,
        Func<string, string?>? getText = null) =>
        Titles[kind switch
        {
            MailMergeInteractivePromptKind.FillIn => MailMergeRuleKind.FillIn,
            MailMergeInteractivePromptKind.Ask => MailMergeRuleKind.Ask,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        }].Resolve(getText);

    public static MailMergeRuleDialogRequest CreateRequest(
        MailMergeRuleKind kind,
        IEnumerable<string>? fieldNames = null,
        Func<string, string?>? getText = null)
    {
        var fields = fieldNames?.ToArray() ?? [];
        var title = Titles[kind].Resolve(getText);
        return kind switch
        {
            MailMergeRuleKind.IfThenElse => new MailMergeRuleIfDialogRequest(
                fields,
                title,
                FieldNameLabel.Resolve(getText),
                ComparisonLabel.Resolve(getText),
                CompareToLabel.Resolve(getText),
                TrueTextLabel.Resolve(getText),
                FalseTextLabel.Resolve(getText)),
            MailMergeRuleKind.SkipRecordIf or MailMergeRuleKind.NextRecordIf =>
                new MailMergeRuleConditionDialogRequest(
                    kind,
                    fields,
                    title,
                    FieldNameLabel.Resolve(getText),
                    ComparisonLabel.Resolve(getText),
                    CompareToLabel.Resolve(getText)),
            MailMergeRuleKind.FillIn => new MailMergeRulePromptDialogRequest(
                kind,
                title,
                FillInPrompt.Resolve(getText)),
            MailMergeRuleKind.Ref => new MailMergeRulePromptDialogRequest(
                kind,
                title,
                RefPrompt.Resolve(getText)),
            MailMergeRuleKind.Ask => new MailMergeRuleNameValueDialogRequest(
                kind,
                title,
                BookmarkNameLabel.Resolve(getText),
                AskPrompt.Resolve(getText)),
            MailMergeRuleKind.Set => new MailMergeRuleNameValueDialogRequest(
                kind,
                title,
                BookmarkNameLabel.Resolve(getText),
                SetValue.Resolve(getText)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

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

    private static ResourceTextDescriptor Text(string resourceKey, string fallbackText) =>
        new(resourceKey, fallbackText);

}

public static class MailMergeRuleAuthoringWorkflow
{
    public static async ValueTask<MailMergeRuleAuthoringExecution> RunAsync(
        MailMergeRuleDialogRequest request,
        MailMergeRuleDialogPresenter showDialog,
        MailMergeRuleInsertionPresenter insertField,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(showDialog);
        ArgumentNullException.ThrowIfNull(insertField);

        var response = await showDialog(request, cancellationToken).ConfigureAwait(false);
        if (response is null)
            return MailMergeRuleAuthoringExecution.Cancelled;

        var plan = CreateInsertionPlan(request, response);
        if (plan is null)
            return new MailMergeRuleAuthoringExecution(true, null);

        await insertField(plan, cancellationToken).ConfigureAwait(false);
        return new MailMergeRuleAuthoringExecution(true, plan);
    }

    public static MailMergeFieldInsertionPlan? CreateInsertionPlan(
        MailMergeRuleDialogRequest request,
        MailMergeRuleDialogResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        return (request.Kind, response) switch
        {
            (MailMergeRuleKind.IfThenElse, MailMergeRuleIfDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateIfPlan(result.Result),
            (MailMergeRuleKind.SkipRecordIf, MailMergeRuleConditionDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateConditionPlan(result.Result, skipRecord: true),
            (MailMergeRuleKind.NextRecordIf, MailMergeRuleConditionDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateConditionPlan(result.Result, skipRecord: false),
            (MailMergeRuleKind.FillIn, MailMergeRulePromptDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateFillInPlan(result.Value),
            (MailMergeRuleKind.Ask, MailMergeRuleNameValueDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateAskPlan(result.Result.Name, result.Result.Value),
            (MailMergeRuleKind.Set, MailMergeRuleNameValueDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateSetPlan(result.Result.Name, result.Result.Value),
            (MailMergeRuleKind.Ref, MailMergeRulePromptDialogResponse result) =>
                MailMergeRuleAuthoringPlanner.CreateRefPlan(result.Value),
            _ => throw new ArgumentException(
                $"Response {response.GetType().Name} does not match request {request.Kind}.",
                nameof(response)),
        };
    }
}
