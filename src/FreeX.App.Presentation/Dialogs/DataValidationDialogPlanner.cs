using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

public enum DataValidationRangeSelectionTarget
{
    Formula1,
    Formula2
}

public sealed record DataValidationRangeSelectionRequest(
    DataValidationRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = false);

public sealed record DataValidationTypeChoice(DvType Type, string Label)
{
    public override string ToString() => Label;
}

public sealed record DataValidationOperatorChoice(DvOperator Operator, string Label)
{
    public override string ToString() => Label;
}

public sealed record DataValidationAlertStyleChoice(DvAlertStyle AlertStyle, string Label)
{
    public override string ToString() => Label;
}

public sealed record DataValidationFormulaFieldDescriptor(
    string LabelResourceKey,
    string HelpText);

public enum DvRuleEditorFocusTarget
{
    Formula1,
    Formula2
}

public sealed record DataValidationRuleEditorVisibilityPlan(
    bool ShowOperator,
    DvFormula1Label Formula1Label,
    bool ShowFormula1,
    bool ShowFormula2,
    bool ShowFormula1RangePicker,
    bool ShowFormula2RangePicker,
    bool ShowFormula1UseSelection,
    bool ShowFormula2UseSelection,
    bool ShowDropdown);

public sealed record DataValidationRuleEditorInput
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DvType Type { get; init; } = DvType.Any;
    public DvOperator Operator { get; init; } = DvOperator.Between;
    public DvAlertStyle AlertStyle { get; init; } = DvAlertStyle.Stop;
    public string? Formula1 { get; init; }
    public string? Formula2 { get; init; }
    public bool AllowBlank { get; init; } = true;
    public bool ShowDropdown { get; init; } = true;
    public bool ApplyToSameSettings { get; init; }
    public bool ShowInputMessage { get; init; } = true;
    public bool ShowErrorMessage { get; init; } = true;
    public string? ErrorTitle { get; init; }
    public string? PromptTitle { get; init; }
    public string? PromptMessage { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the rule being edited originated from (or must continue to be written to) the
    /// worksheet x14 extLst block. Carried over from the existing rule so re-saving an unchanged
    /// (or edited) cross-sheet List validation doesn't silently downgrade it to a broken legacy rule.
    /// </summary>
    public bool IsX14 { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }
    public IReadOnlyList<string>? NativeChildXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeContainerAttributes { get; init; }
    public IReadOnlyList<string>? NativeContainerChildXmls { get; init; }
}

public static class DataValidationDialogPlanner
{
    private static readonly (DvType Type, string ResourceKey)[] TypeChoiceDescriptors =
    [
        (DvType.Any, "DataValidation_AnyValue"),
        (DvType.WholeNumber, "DataValidation_WholeNumber"),
        (DvType.Decimal, "DataValidation_Decimal"),
        (DvType.List, "DataValidation_List"),
        (DvType.Date, "DataValidation_Date"),
        (DvType.Time, "DataValidation_Time"),
        (DvType.TextLength, "DataValidation_TextLength"),
        (DvType.Custom, "DataValidation_Custom")
    ];

    private static readonly (DvOperator Operator, string ResourceKey)[] OperatorChoiceDescriptors =
    [
        (DvOperator.Between, "DataValidation_Between"),
        (DvOperator.NotBetween, "DataValidation_NotBetween"),
        (DvOperator.Equal, "DataValidation_EqualTo"),
        (DvOperator.NotEqual, "DataValidation_NotEqualTo"),
        (DvOperator.GreaterThan, "DataValidation_GreaterThan"),
        (DvOperator.LessThan, "DataValidation_LessThan"),
        (DvOperator.GreaterThanOrEqual, "DataValidation_GreaterThanOrEqual"),
        (DvOperator.LessThanOrEqual, "DataValidation_LessThanOrEqual")
    ];

    private static readonly (DvAlertStyle AlertStyle, string ResourceKey)[] AlertStyleChoiceDescriptors =
    [
        (DvAlertStyle.Stop, "DataValidation_Stop"),
        (DvAlertStyle.Warning, "DataValidation_Warning"),
        (DvAlertStyle.Information, "DataValidation_Information")
    ];

    public static IReadOnlyList<DataValidationTypeChoice> CreateTypeChoices(Func<string, string> getText) =>
        ResolveChoices(TypeChoiceDescriptors, getText, item => new DataValidationTypeChoice(item.Value, item.Label));

    public static IReadOnlyList<DataValidationOperatorChoice> CreateOperatorChoices(Func<string, string> getText) =>
        ResolveChoices(OperatorChoiceDescriptors, getText, item => new DataValidationOperatorChoice(item.Value, item.Label));

    public static IReadOnlyList<DataValidationAlertStyleChoice> CreateAlertStyleChoices(Func<string, string> getText) =>
        ResolveChoices(AlertStyleChoiceDescriptors, getText, item => new DataValidationAlertStyleChoice(item.Value, item.Label));

    public static DataValidationFormulaFieldDescriptor GetFormula1FieldDescriptor(DvFormula1Label label) =>
        label switch
        {
            DvFormula1Label.Source => new(
                "DataValidation_Source",
                "List source range or comma-separated values."),
            DvFormula1Label.Formula => new(
                "DataValidation_Formula",
                "Formula that must evaluate to TRUE (e.g. =A1>0)."),
            DvFormula1Label.Value => new(
                "DataValidation_Value",
                "Value for the validation rule."),
            _ => new(
                "DataValidation_Minimum",
                "Minimum value for the validation rule.")
        };

    public static DataValidationFormulaFieldDescriptor Formula2FieldDescriptor { get; } =
        new("DataValidation_Maximum", "Maximum value for the validation rule.");

    public static DataValidation CreateDefaultRule(DvType type, GridRange selectedRange)
    {
        var op = DefaultOperatorForType(type);
        return new DataValidation
        {
            AppliesTo = selectedRange,
            Type = type,
            Operator = op,
            Formula1 = DefaultFormula1ForType(type),
            Formula2 = DefaultFormula2ForType(type),
            AllowBlank = true,
            ShowDropdown = type == DvType.List,
            AlertStyle = DvAlertStyle.Stop,
            ShowInputMessage = true,
            ShowErrorMessage = true,
            ErrorTitle = "",
            ErrorMessage = "",
            PromptTitle = "",
            PromptMessage = ""
        };
    }

    public static DvOperator DefaultOperatorForType(DvType type) =>
        type == DvType.TextLength
            ? DvOperator.LessThanOrEqual
            : DvOperator.Between;

    public static string DefaultFormula1ForType(DvType type) => type switch
    {
        DvType.List => "Yes,No",
        DvType.TextLength => "50",
        DvType.Decimal => "0",
        DvType.Date => "2024-01-01",
        DvType.Time => "09:00",
        DvType.Custom => "=A1>0",
        DvType.Any => "",
        _ => "1",
    };

    public static string DefaultFormula2ForType(DvType type) => type switch
    {
        DvType.WholeNumber => "100",
        DvType.Decimal => "100",
        DvType.Date => "2024-12-31",
        DvType.Time => "17:00",
        _ => "",
    };

    public static DataValidationRuleEditorVisibilityPlan CreateVisibilityPlan(
        DvType type,
        DvOperator op,
        bool hasSelectionSource)
    {
        var model = DataValidationDialogModel.ForType(type);
        var showFormula1 = model.HasField(DvInputField.Formula1);
        var showFormula2 = model.ShowsFormula2(op);

        return new DataValidationRuleEditorVisibilityPlan(
            ShowOperator: model.ShowsOperator,
            Formula1Label: model.Formula1LabelFor(op),
            ShowFormula1: showFormula1,
            ShowFormula2: showFormula2,
            ShowFormula1RangePicker: showFormula1,
            ShowFormula2RangePicker: showFormula2,
            ShowFormula1UseSelection: showFormula1 && hasSelectionSource,
            ShowFormula2UseSelection: showFormula2 && hasSelectionSource,
            ShowDropdown: model.ShowsDropdown);
    }

    public static DvValidationResult ValidateCriteria(
        DvType type,
        DvOperator op,
        string? formula1,
        string? formula2) =>
        DataValidationDialogModel.ForType(type).Validate(new DvCriteriaInput
        {
            Type = type,
            Operator = op,
            Formula1 = formula1,
            Formula2 = formula2
        });

    public static DvRuleEditorFocusTarget FocusTargetForInvalidCriteria(
        DvType type,
        DvOperator op,
        string? formula1,
        string? formula2)
    {
        if (!RequiresSecondFormula(type, op))
            return DvRuleEditorFocusTarget.Formula1;

        var second = formula2?.Trim() ?? "";
        if (second.Length == 0)
            return DvRuleEditorFocusTarget.Formula2;

        var first = formula1?.Trim() ?? "";
        var firstResult = ValidateCriteria(type, DvOperator.Equal, first, null);
        var secondResult = ValidateCriteria(type, DvOperator.Equal, second, null);
        return firstResult.IsValid && !secondResult.IsValid
            ? DvRuleEditorFocusTarget.Formula2
            : DvRuleEditorFocusTarget.Formula1;
    }

    public static bool RequiresSecondFormula(DvType type, DvOperator op) =>
        DataValidationDialogModel.ForType(type).ShowsFormula2(op);

    public static string NormalizeFormula1(DvType type, string? formula1) =>
        type == DvType.Any
            ? ""
            : formula1?.Trim() ?? "";

    public static string NormalizeFormula2(DvType type, DvOperator op, string? formula2) =>
        RequiresSecondFormula(type, op)
            ? formula2?.Trim() ?? ""
            : "";

    public static DataValidation CreateRule(DataValidationRuleEditorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var formula1 = NormalizeFormula1(input.Type, input.Formula1);

        return new DataValidation
        {
            Id = input.Id,
            Type = input.Type,
            Operator = input.Operator,
            Formula1 = formula1,
            Formula2 = NormalizeFormula2(input.Type, input.Operator, input.Formula2),
            AllowBlank = input.AllowBlank,
            ShowDropdown = input.Type == DvType.List && input.ShowDropdown,
            AlertStyle = input.AlertStyle,
            ShowInputMessage = input.ShowInputMessage,
            ShowErrorMessage = input.ShowErrorMessage,
            ErrorTitle = input.ErrorTitle?.Trim() ?? "",
            PromptTitle = input.PromptTitle?.Trim() ?? "",
            PromptMessage = input.PromptMessage?.Trim() ?? "",
            ErrorMessage = input.ErrorMessage?.Trim() ?? "",
            // Carried over verbatim if already true (never silently downgraded), and additionally
            // auto-promoted for a freshly authored List source that Excel cannot evaluate from the
            // legacy <dataValidation><formula1> slot: a formula-based source referencing another
            // sheet (e.g. "=Sheet2!$A$1:$A$5") or one too long for the legacy element. Without this,
            // a brand-new cross-sheet List rule would be saved with an inert legacy formula that
            // Excel never reads. See DataValidation.IsX14 doc comment.
            IsX14 = input.IsX14 || RequiresX14ForListSource(input.Type, formula1),
            NativeAttributes = input.NativeAttributes,
            NativeChildXmls = input.NativeChildXmls,
            NativeContainerAttributes = input.NativeContainerAttributes,
            NativeContainerChildXmls = input.NativeContainerChildXmls
        };
    }

    /// <summary>
    /// True when a List rule's source formula needs the x14 extLst path because the legacy
    /// &lt;dataValidation&gt;&lt;formula1&gt; slot can't carry it: a formula (not a literal
    /// comma-separated item list) that references another sheet, or any source too long for the
    /// legacy element (Excel's 255-character limit on the classic formula1 text).
    /// </summary>
    private static bool RequiresX14ForListSource(DvType type, string formula1)
    {
        if (type != DvType.List || formula1.Length == 0)
            return false;

        if (formula1.Length > 255)
            return true;

        return formula1.StartsWith('=') && formula1.Contains('!');
    }

    public static bool IsClearAllState(DataValidationRuleEditorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Type == DvType.Any
            && input.Operator == DvOperator.Between
            && input.AlertStyle == DvAlertStyle.Stop
            && string.IsNullOrWhiteSpace(input.Formula1)
            && string.IsNullOrWhiteSpace(input.Formula2)
            && input.AllowBlank
            && input.ShowDropdown
            && !input.ApplyToSameSettings
            && input.ShowInputMessage
            && input.ShowErrorMessage
            && string.IsNullOrWhiteSpace(input.ErrorTitle)
            && string.IsNullOrWhiteSpace(input.PromptTitle)
            && string.IsNullOrWhiteSpace(input.PromptMessage)
            && string.IsNullOrWhiteSpace(input.ErrorMessage);
    }

    public static DataValidationRangeSelectionRequest CreateRangeSelectionRequest(
        DataValidationRangeSelectionTarget target,
        string currentText,
        bool collapseDialog = false) =>
        new(target, currentText.Trim(), collapseDialog);

    public static DvType TypeFromTag(string? tag) => tag switch
    {
        "List" => DvType.List,
        "WholeNumber" => DvType.WholeNumber,
        "Decimal" => DvType.Decimal,
        "Date" => DvType.Date,
        "Time" => DvType.Time,
        "TextLength" => DvType.TextLength,
        "Custom" => DvType.Custom,
        _ => DvType.Any
    };

    public static string TypeTag(DvType type) => type switch
    {
        DvType.List => "List",
        DvType.WholeNumber => "WholeNumber",
        DvType.Decimal => "Decimal",
        DvType.Date => "Date",
        DvType.Time => "Time",
        DvType.TextLength => "TextLength",
        DvType.Custom => "Custom",
        _ => "Any"
    };

    public static DvOperator OperatorFromTag(string? tag) => tag switch
    {
        "NotBetween" => DvOperator.NotBetween,
        "Equal" => DvOperator.Equal,
        "NotEqual" => DvOperator.NotEqual,
        "GreaterThan" => DvOperator.GreaterThan,
        "LessThan" => DvOperator.LessThan,
        "GreaterThanOrEqual" => DvOperator.GreaterThanOrEqual,
        "LessThanOrEqual" => DvOperator.LessThanOrEqual,
        _ => DvOperator.Between
    };

    public static string OperatorTag(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "NotBetween",
        DvOperator.Equal => "Equal",
        DvOperator.NotEqual => "NotEqual",
        DvOperator.GreaterThan => "GreaterThan",
        DvOperator.LessThan => "LessThan",
        DvOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        DvOperator.LessThanOrEqual => "LessThanOrEqual",
        _ => "Between"
    };

    public static DvAlertStyle AlertStyleFromTag(string? tag) => tag switch
    {
        "Warning" => DvAlertStyle.Warning,
        "Information" => DvAlertStyle.Information,
        _ => DvAlertStyle.Stop
    };

    public static string AlertStyleTag(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "Warning",
        DvAlertStyle.Information => "Information",
        _ => "Stop"
    };

    private static IReadOnlyList<TResult> ResolveChoices<TValue, TResult>(
        IReadOnlyList<(TValue Value, string ResourceKey)> descriptors,
        Func<string, string> getText,
        Func<(TValue Value, string Label), TResult> createChoice)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(createChoice);

        return descriptors
            .Select(descriptor => createChoice((descriptor.Value, getText(descriptor.ResourceKey))))
            .ToArray();
    }
}
