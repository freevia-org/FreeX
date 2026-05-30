using System.Windows.Media;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private static string[] FormatStyleLabels =>
        [
            UiText.Get("ConditionalFormatDialog_FormatStyle_DataBar"),
            UiText.Get("ConditionalFormatDialog_FormatStyle_2ColorScale"),
            UiText.Get("ConditionalFormatDialog_FormatStyle_3ColorScale"),
            UiText.Get("ConditionalFormatDialog_FormatStyle_IconSet")
        ];

    private string CurrentFormatStyleLabel => _ruleType switch
    {
        "Icon Set"    => UiText.Get("ConditionalFormatDialog_FormatStyle_IconSet"),
        "Color Scale" => _colorScaleUseThreeColorBox?.IsChecked == true
            ? UiText.Get("ConditionalFormatDialog_FormatStyle_3ColorScale")
            : UiText.Get("ConditionalFormatDialog_FormatStyle_2ColorScale"),
        _             => UiText.Get("ConditionalFormatDialog_FormatStyle_DataBar")
    };

    private static (string Label, Color FillColor, Color? FontColor, bool Bold)[] ColorOptions =>
        [
            (UiText.Get("ConditionalFormatDialog_FormatPreset_LightRedDarkRedText"), Color.FromRgb(255, 199, 206), Color.FromRgb(156, 0, 6), true),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_YellowDarkYellowText"), Color.FromRgb(255, 235, 132), Color.FromRgb(156, 101, 0), true),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_GreenDarkGreenText"), Color.FromRgb(198, 239, 206), Color.FromRgb(0, 97, 0), true),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_LightRedFill"), Color.FromRgb(255, 199, 206), null, false),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_YellowFill"), Color.FromRgb(255, 235, 132), null, false),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_GreenFill"), Color.FromRgb(198, 239, 206), null, false),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_LightBlueFill"), Color.FromRgb(189, 215, 238), null, false),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_BoldRedText"), Color.FromRgb(255, 255, 255), Color.FromRgb(255, 0, 0), true),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_BoldGreenText"), Color.FromRgb(255, 255, 255), Color.FromRgb(0, 176, 80), true),
            (UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat"), Color.FromRgb(255, 255, 255), null, false),
        ];

    private static string[] ExcelRuleShellTypes =>
        [
            UiText.Get("ConditionalFormatDialog_RuleShell_FormatAllCells"),
            UiText.Get("ConditionalFormatDialog_RuleShell_FormatContainingCells"),
            UiText.Get("ConditionalFormatDialog_RuleShell_FormatTopBottom"),
            UiText.Get("ConditionalFormatDialog_RuleShell_FormatAboveBelowAverage"),
            UiText.Get("ConditionalFormatDialog_RuleShell_FormatUniqueDuplicate"),
            UiText.Get("ConditionalFormatDialog_RuleShell_UseFormula")
        ];

    private static string[] ConditionKindLabels =>
        [
            UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_Blanks"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_NoBlanks"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_Errors"),
            UiText.Get("ConditionalFormatDialog_ConditionKind_NoErrors")
        ];

    private static (string Label, string RuleType)[] CellValueOperatorLabels =>
        [
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThan"), "Greater Than"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_LessThan"), "Less Than"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_EqualTo"), "Equal To"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_Between"), "Between"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_NotEqualTo"), "Not Equal To"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThanOrEqualTo"), "Greater Than Or Equal To"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_LessThanOrEqualTo"), "Less Than Or Equal To"),
            (UiText.Get("ConditionalFormatDialog_CellValueOperator_NotBetween"), "Not Between")
        ];

    private static (string Label, string RuleType)[] SpecificTextOperatorLabels =>
        [
            (UiText.Get("ConditionalFormatDialog_TextOperator_Containing"), "Text Contains"),
            (UiText.Get("ConditionalFormatDialog_TextOperator_NotContaining"), "Text Does Not Contain"),
            (UiText.Get("ConditionalFormatDialog_TextOperator_BeginningWith"), "Text Begins With"),
            (UiText.Get("ConditionalFormatDialog_TextOperator_EndingWith"), "Text Ends With")
        ];

    private static readonly IReadOnlyList<string> IconSetStyles = ConditionalFormatIconSetPlanner.Styles;

    private static (string Label, string Value)[] DateOccurringPeriods =>
        [
            (UiText.Get("ConditionalFormatDialog_DatePeriod_Yesterday"), "yesterday"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_Today"), "today"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_Tomorrow"), "tomorrow"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_Last7Days"), "last7Days"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_LastWeek"), "lastWeek"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_ThisWeek"), "thisWeek"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_NextWeek"), "nextWeek"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_LastMonth"), "lastMonth"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_ThisMonth"), "thisMonth"),
            (UiText.Get("ConditionalFormatDialog_DatePeriod_NextMonth"), "nextMonth")
        ];

    private static string ConditionKindLabelForRuleType(string ruleType) => ruleType switch
    {
        "Text Contains" or "Text Does Not Contain" or "Text Begins With" or "Text Ends With" => UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText"),
        "Date Occurring" => UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring"),
        "Blanks" => UiText.Get("ConditionalFormatDialog_ConditionKind_Blanks"),
        "No Blanks" => UiText.Get("ConditionalFormatDialog_ConditionKind_NoBlanks"),
        "Errors" => UiText.Get("ConditionalFormatDialog_ConditionKind_Errors"),
        "No Errors" => UiText.Get("ConditionalFormatDialog_ConditionKind_NoErrors"),
        _ => UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue")
    };

    private static string DefaultRuleTypeForConditionKind(string label) => label switch
    {
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText") => "Text Contains",
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring") => "Date Occurring",
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_Blanks") => "Blanks",
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_NoBlanks") => "No Blanks",
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_Errors") => "Errors",
        var value when value == UiText.Get("ConditionalFormatDialog_ConditionKind_NoErrors") => "No Errors",
        _ => "Greater Than"
    };

    private static string CellValueOperatorLabelForRuleType(string ruleType) =>
        CellValueOperatorLabels.FirstOrDefault(item => item.RuleType == ruleType).Label ?? UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThan");

    private static string SpecificTextOperatorLabelForRuleType(string ruleType) =>
        SpecificTextOperatorLabels.FirstOrDefault(item => item.RuleType == ruleType).Label ?? UiText.Get("ConditionalFormatDialog_TextOperator_Containing");

    protected static string RuleTypeDisplayName(string ruleType) => ruleType switch
    {
        "Formula" or "Use a Formula" => UiText.Get("ConditionalFormatDialog_RuleType_Formula"),
        "Data Bar" => UiText.Get("ConditionalFormatDialog_RuleType_DataBar"),
        "Color Scale" => UiText.Get("ConditionalFormatDialog_RuleType_ColorScale"),
        "Icon Set" => UiText.Get("ConditionalFormatDialog_RuleType_IconSet"),
        "Text Contains" => UiText.Get("ConditionalFormatDialog_RuleType_TextContains"),
        "Text Does Not Contain" => UiText.Get("ConditionalFormatDialog_RuleType_TextDoesNotContain"),
        "Text Begins With" => UiText.Get("ConditionalFormatDialog_RuleType_TextBeginsWith"),
        "Text Ends With" => UiText.Get("ConditionalFormatDialog_RuleType_TextEndsWith"),
        "Date Occurring" => UiText.Get("ConditionalFormatDialog_RuleType_DateOccurring"),
        "Duplicate Values" => UiText.Get("ConditionalFormatDialog_RuleType_DuplicateValues"),
        "Blanks" => UiText.Get("ConditionalFormatDialog_RuleType_Blanks"),
        "No Blanks" => UiText.Get("ConditionalFormatDialog_RuleType_NoBlanks"),
        "Errors" => UiText.Get("ConditionalFormatDialog_RuleType_Errors"),
        "No Errors" => UiText.Get("ConditionalFormatDialog_RuleType_NoErrors"),
        "Above Average" => UiText.Get("ConditionalFormatDialog_RuleType_AboveAverage"),
        "Below Average" => UiText.Get("ConditionalFormatDialog_RuleType_BelowAverage"),
        "Top 10%" => UiText.Get("ConditionalFormatDialog_RuleType_Top10Percent"),
        "Bottom 10%" => UiText.Get("ConditionalFormatDialog_RuleType_Bottom10Percent"),
        "Top 10 Items" => UiText.Get("ConditionalFormatDialog_RuleType_Top10Items"),
        "Bottom 10 Items" => UiText.Get("ConditionalFormatDialog_RuleType_Bottom10Items"),
        "Greater Than" => UiText.Get("ConditionalFormatDialog_RuleType_GreaterThan"),
        "Less Than" => UiText.Get("ConditionalFormatDialog_RuleType_LessThan"),
        "Equal To" => UiText.Get("ConditionalFormatDialog_RuleType_EqualTo"),
        "Between" => UiText.Get("ConditionalFormatDialog_RuleType_Between"),
        "Not Equal To" => UiText.Get("ConditionalFormatDialog_RuleType_NotEqualTo"),
        "Greater Than Or Equal To" => UiText.Get("ConditionalFormatDialog_RuleType_GreaterThanOrEqualTo"),
        "Less Than Or Equal To" => UiText.Get("ConditionalFormatDialog_RuleType_LessThanOrEqualTo"),
        "Not Between" => UiText.Get("ConditionalFormatDialog_RuleType_NotBetween"),
        _ => ruleType
    };
}
