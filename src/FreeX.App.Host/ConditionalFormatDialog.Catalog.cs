using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private static string[] FormatStyleLabels =>
        Localize(ConditionalFormatDialogCatalog.FormatStyleOptions);

    private string CurrentFormatStyleLabel =>
        UiText.Get(ConditionalFormatDialogCatalog.FormatStyleKeyForRuleType(
            _ruleType,
            _colorScaleUseThreeColorBox?.IsChecked == true));

    private static IReadOnlyList<ConditionalFormatDialogColorPreset> ColorOptions =>
        ConditionalFormatDialogCatalog.ColorPresets;

    private static string[] ExcelRuleShellTypes =>
        Localize(ConditionalFormatDialogCatalog.RuleShellOptions);

    private static string[] ConditionKindLabels =>
        Localize(ConditionalFormatDialogCatalog.ConditionKindOptions);

    private static (string Label, string RuleType)[] CellValueOperatorLabels =>
        LocalizeWithRuleType(ConditionalFormatDialogCatalog.CellValueOperatorOptions);

    private static (string Label, string RuleType)[] SpecificTextOperatorLabels =>
        LocalizeWithRuleType(ConditionalFormatDialogCatalog.SpecificTextOperatorOptions);

    private static readonly IReadOnlyList<string> IconSetStyles = ConditionalFormatIconSetCatalog.GalleryStyles;

    private static (string Label, string Value)[] DateOccurringPeriods =>
        ConditionalFormatDialogCatalog.DatePeriodOptions
            .Select(option => (UiText.Get(option.LabelKey), option.Value))
            .ToArray();

    private static string ConditionKindLabelForRuleType(string ruleType) =>
        UiText.Get(ConditionalFormatDialogCatalog.ConditionKindKeyForRuleType(ruleType));

    private static string DefaultRuleTypeForConditionKind(string label) =>
        ConditionalFormatDialogCatalog.DefaultRuleTypeForConditionKindKey(
            LabelKeyForLocalizedOption(ConditionalFormatDialogCatalog.ConditionKindOptions, label));

    private static string CellValueOperatorLabelForRuleType(string ruleType)
    {
        foreach (var item in CellValueOperatorLabels)
        {
            if (item.RuleType == ruleType)
            {
                return item.Label ?? UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThan");
            }
        }

        return UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThan");
    }

    private static string SpecificTextOperatorLabelForRuleType(string ruleType)
    {
        foreach (var item in SpecificTextOperatorLabels)
        {
            if (item.RuleType == ruleType)
            {
                return item.Label ?? UiText.Get("ConditionalFormatDialog_TextOperator_Containing");
            }
        }

        return UiText.Get("ConditionalFormatDialog_TextOperator_Containing");
    }

    protected static string RuleTypeDisplayName(string ruleType) =>
        ConditionalFormatDialogCatalog.RuleTypeDisplayNameKey(ruleType) is { } key
            ? UiText.Get(key)
            : ruleType;

    private static string[] Localize(IReadOnlyList<ConditionalFormatDialogOption> options) =>
        options.Select(option => UiText.Get(option.LabelKey)).ToArray();

    private static string[] Localize(IReadOnlyList<ConditionalFormatDialogFormatStyleOption> options) =>
        options.Select(option => UiText.Get(option.LabelKey)).ToArray();

    private static (string Label, string RuleType)[] LocalizeWithRuleType(
        IReadOnlyList<ConditionalFormatDialogOption> options) =>
        options.Select(option => (UiText.Get(option.LabelKey), option.RuleType)).ToArray();

    private static string? LabelKeyForLocalizedOption(
        IReadOnlyList<ConditionalFormatDialogOption> options,
        string? label)
    {
        foreach (var option in options)
        {
            if (label == UiText.Get(option.LabelKey))
                return option.LabelKey;
        }

        return null;
    }

    private static string? LabelKeyForLocalizedFormatStyle(string? label)
    {
        foreach (var option in ConditionalFormatDialogCatalog.FormatStyleOptions)
        {
            if (label == UiText.Get(option.LabelKey))
                return option.LabelKey;
        }

        return null;
    }
}
