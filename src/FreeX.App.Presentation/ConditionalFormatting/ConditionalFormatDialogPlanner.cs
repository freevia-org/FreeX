using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public sealed record ConditionalFormatDialogOption(string LabelKey, string RuleType);

public sealed record ConditionalFormatDialogFormatStyleOption(
    string LabelKey,
    string RuleType,
    bool UseThreeColorScale = false);

public sealed record ConditionalFormatDialogColorPreset(
    string LabelKey,
    CellColor FillColor,
    CellColor? FontColor,
    bool Bold,
    bool IsCustom = false)
{
    public CellStyle ToCellStyle()
    {
        var style = new CellStyle
        {
            FillColor = FillColor,
            Bold = Bold
        };

        if (FontColor is { } font)
            style.FontColor = font;

        return style;
    }
}

public sealed record ConditionalFormatDialogDatePeriodOption(string LabelKey, string Value);

public sealed record ConditionalFormatDialogAxisPositionOption(string LabelKey, string? XmlValue);

public sealed record ConditionalFormatRuleTypeOption(string LabelKey, CfRuleType RuleType);

public sealed record ConditionalFormatOperatorOption(string LabelKey, CfOperator Operator);

public enum ConditionalFormatDialogFamily
{
    NewRule,
    HighlightCells,
    TopBottom,
    DataBar,
    ColorScale,
    IconSet
}

/// <summary>
/// Portable catalog and default-selection policy for conditional-format rule dialogs. Renderers localize
/// <see cref="ConditionalFormatDialogOption.LabelKey"/> at binding edges; rule names and XML values
/// remain stable shared tokens.
/// </summary>
public static class ConditionalFormatDialogCatalog
{
    public const double RuleEditorWpfWindowWidth = 650;
    public const double RuleEditorCaptureWidth = 634;
    public const double RuleEditorCaptureHeight = 334;
    public const double RuleEditorMinWidth = 600;
    public const double RuleEditorMinHeight = 320;

    public const string FormulaRule = "Formula";
    public const string UseFormulaRule = "Use a Formula";
    public const string DataBarRule = "Data Bar";
    public const string ColorScaleRule = "Color Scale";
    public const string IconSetRule = "Icon Set";
    public const string GreaterThanRule = "Greater Than";
    public const string LessThanRule = "Less Than";
    public const string EqualToRule = "Equal To";
    public const string BetweenRule = "Between";
    public const string NotEqualToRule = "Not Equal To";
    public const string GreaterThanOrEqualToRule = "Greater Than Or Equal To";
    public const string LessThanOrEqualToRule = "Less Than Or Equal To";
    public const string NotBetweenRule = "Not Between";
    public const string TextContainsRule = "Text Contains";
    public const string TextDoesNotContainRule = "Text Does Not Contain";
    public const string TextBeginsWithRule = "Text Begins With";
    public const string TextEndsWithRule = "Text Ends With";
    public const string DateOccurringRule = "Date Occurring";
    public const string DuplicateValuesRule = "Duplicate Values";
    public const string BlanksRule = "Blanks";
    public const string NoBlanksRule = "No Blanks";
    public const string ErrorsRule = "Errors";
    public const string NoErrorsRule = "No Errors";
    public const string AboveAverageRule = "Above Average";
    public const string BelowAverageRule = "Below Average";
    public const string Top10PercentRule = "Top 10%";
    public const string Bottom10PercentRule = "Bottom 10%";
    public const string Top10ItemsRule = "Top 10 Items";
    public const string Bottom10ItemsRule = "Bottom 10 Items";

    public static IReadOnlyList<ConditionalFormatDialogFormatStyleOption> FormatStyleOptions { get; } =
    [
        new("ConditionalFormatDialog_FormatStyle_DataBar", DataBarRule),
        new("ConditionalFormatDialog_FormatStyle_2ColorScale", ColorScaleRule),
        new("ConditionalFormatDialog_FormatStyle_3ColorScale", ColorScaleRule, UseThreeColorScale: true),
        new("ConditionalFormatDialog_FormatStyle_IconSet", IconSetRule)
    ];

    public static IReadOnlyList<ConditionalFormatDialogColorPreset> ColorPresets { get; } =
    [
        new("ConditionalFormatDialog_FormatPreset_LightRedDarkRedText", new CellColor(255, 199, 206), new CellColor(156, 0, 6), true),
        new("ConditionalFormatDialog_FormatPreset_YellowDarkYellowText", new CellColor(255, 235, 132), new CellColor(156, 101, 0), true),
        new("ConditionalFormatDialog_FormatPreset_GreenDarkGreenText", new CellColor(198, 239, 206), new CellColor(0, 97, 0), true),
        new("ConditionalFormatDialog_FormatPreset_LightRedFill", new CellColor(255, 199, 206), null, false),
        new("ConditionalFormatDialog_FormatPreset_YellowFill", new CellColor(255, 235, 132), null, false),
        new("ConditionalFormatDialog_FormatPreset_GreenFill", new CellColor(198, 239, 206), null, false),
        new("ConditionalFormatDialog_FormatPreset_LightBlueFill", new CellColor(189, 215, 238), null, false),
        new("ConditionalFormatDialog_FormatPreset_BoldRedText", new CellColor(255, 255, 255), new CellColor(255, 0, 0), true),
        new("ConditionalFormatDialog_FormatPreset_BoldGreenText", new CellColor(255, 255, 255), new CellColor(0, 176, 80), true),
        new("ConditionalFormatDialog_FormatPreset_CustomFormat", new CellColor(255, 255, 255), null, false, IsCustom: true),
    ];

    public static IReadOnlyList<ConditionalFormatDialogOption> RuleShellOptions { get; } =
    [
        new("ConditionalFormatDialog_RuleShell_FormatAllCells", DataBarRule),
        new("ConditionalFormatDialog_RuleShell_FormatContainingCells", GreaterThanRule),
        new("ConditionalFormatDialog_RuleShell_FormatTopBottom", Top10ItemsRule),
        new("ConditionalFormatDialog_RuleShell_FormatAboveBelowAverage", AboveAverageRule),
        new("ConditionalFormatDialog_RuleShell_FormatUniqueDuplicate", DuplicateValuesRule),
        new("ConditionalFormatDialog_RuleShell_UseFormula", FormulaRule)
    ];

    public static IReadOnlyList<ConditionalFormatDialogOption> ConditionKindOptions { get; } =
    [
        new("ConditionalFormatDialog_ConditionKind_CellValue", GreaterThanRule),
        new("ConditionalFormatDialog_ConditionKind_SpecificText", TextContainsRule),
        new("ConditionalFormatDialog_ConditionKind_DatesOccurring", DateOccurringRule),
        new("ConditionalFormatDialog_ConditionKind_Blanks", BlanksRule),
        new("ConditionalFormatDialog_ConditionKind_NoBlanks", NoBlanksRule),
        new("ConditionalFormatDialog_ConditionKind_Errors", ErrorsRule),
        new("ConditionalFormatDialog_ConditionKind_NoErrors", NoErrorsRule)
    ];

    public static IReadOnlyList<ConditionalFormatDialogOption> CellValueOperatorOptions { get; } =
    [
        new("ConditionalFormatDialog_CellValueOperator_GreaterThan", GreaterThanRule),
        new("ConditionalFormatDialog_CellValueOperator_LessThan", LessThanRule),
        new("ConditionalFormatDialog_CellValueOperator_EqualTo", EqualToRule),
        new("ConditionalFormatDialog_CellValueOperator_Between", BetweenRule),
        new("ConditionalFormatDialog_CellValueOperator_NotEqualTo", NotEqualToRule),
        new("ConditionalFormatDialog_CellValueOperator_GreaterThanOrEqualTo", GreaterThanOrEqualToRule),
        new("ConditionalFormatDialog_CellValueOperator_LessThanOrEqualTo", LessThanOrEqualToRule),
        new("ConditionalFormatDialog_CellValueOperator_NotBetween", NotBetweenRule)
    ];

    public static IReadOnlyList<ConditionalFormatDialogOption> SpecificTextOperatorOptions { get; } =
    [
        new("ConditionalFormatDialog_TextOperator_Containing", TextContainsRule),
        new("ConditionalFormatDialog_TextOperator_NotContaining", TextDoesNotContainRule),
        new("ConditionalFormatDialog_TextOperator_BeginningWith", TextBeginsWithRule),
        new("ConditionalFormatDialog_TextOperator_EndingWith", TextEndsWithRule)
    ];

    public static IReadOnlyList<ConditionalFormatRuleTypeOption> RuleEditorTypeOptions { get; } =
    [
        new("ConditionalFormatDialog_RuleType_CellValue", CfRuleType.CellValue),
        new("ConditionalFormatDialog_RuleType_Formula", CfRuleType.Formula),
        new("ConditionalFormatDialog_RuleType_TopBottom", CfRuleType.Top10),
        new("ConditionalFormatDialog_RuleType_IconSet", CfRuleType.IconSet),
        new("ConditionalFormatDialog_RuleType_DataBar", CfRuleType.DataBar),
        new("ConditionalFormatDialog_RuleType_ColorScale", CfRuleType.ColorScale),
        new("ConditionalFormatDialog_RuleType_TextContains", CfRuleType.ContainsText),
        new("ConditionalFormatDialog_RuleType_DateOccurring", CfRuleType.DateOccurring),
        new("ConditionalFormatDialog_RuleType_DuplicateValues", CfRuleType.DuplicateValues),
        new("ConditionalFormatDialog_RuleType_UniqueValues", CfRuleType.UniqueValues),
        new("ConditionalFormatDialog_RuleType_AboveAverage", CfRuleType.AboveAverage),
    ];

    public static IReadOnlyList<ConditionalFormatRuleTypeOption> RuleEditorShellOptions { get; } =
    [
        new("ConditionalFormatDialog_RuleShell_FormatAllCells", CfRuleType.ColorScale),
        new("ConditionalFormatDialog_RuleShell_FormatContainingCells", CfRuleType.CellValue),
        new("ConditionalFormatDialog_RuleShell_FormatTopBottom", CfRuleType.Top10),
        new("ConditionalFormatDialog_RuleShell_FormatAboveBelowAverage", CfRuleType.AboveAverage),
        new("ConditionalFormatDialog_RuleShell_FormatUniqueDuplicate", CfRuleType.DuplicateValues),
        new("ConditionalFormatDialog_RuleShell_UseFormula", CfRuleType.Formula),
    ];

    public static IReadOnlyList<ConditionalFormatOperatorOption> RuleEditorOperatorOptions { get; } =
    [
        new("ConditionalFormatDialog_CellValueOperator_GreaterThan", CfOperator.GreaterThan),
        new("ConditionalFormatDialog_CellValueOperator_LessThan", CfOperator.LessThan),
        new("ConditionalFormatDialog_CellValueOperator_GreaterThanOrEqualTo", CfOperator.GreaterThanOrEqual),
        new("ConditionalFormatDialog_CellValueOperator_LessThanOrEqualTo", CfOperator.LessThanOrEqual),
        new("ConditionalFormatDialog_CellValueOperator_EqualTo", CfOperator.Equal),
        new("ConditionalFormatDialog_CellValueOperator_NotEqualTo", CfOperator.NotEqual),
        new("ConditionalFormatDialog_CellValueOperator_Between", CfOperator.Between),
        new("ConditionalFormatDialog_CellValueOperator_NotBetween", CfOperator.NotBetween),
    ];

    public static int RuleEditorShellIndexForModelRuleType(CfRuleType ruleType) => ruleType switch
    {
        CfRuleType.ColorScale or CfRuleType.DataBar or CfRuleType.IconSet => 0,
        CfRuleType.Top10 => 2,
        CfRuleType.AboveAverage => 3,
        CfRuleType.DuplicateValues or CfRuleType.UniqueValues => 4,
        CfRuleType.Formula => 5,
        _ => 1,
    };

    public static ConditionalFormatDialogFamily DialogFamilyForRuleType(string ruleType) =>
        ruleType switch
        {
            GreaterThanRule or LessThanRule or EqualToRule or BetweenRule or TextContainsRule or
                DateOccurringRule or DuplicateValuesRule or BlanksRule or NoBlanksRule or ErrorsRule or
                NoErrorsRule => ConditionalFormatDialogFamily.HighlightCells,
            Top10ItemsRule or Bottom10ItemsRule or Top10PercentRule or Bottom10PercentRule or
                AboveAverageRule or BelowAverageRule => ConditionalFormatDialogFamily.TopBottom,
            DataBarRule => ConditionalFormatDialogFamily.DataBar,
            ColorScaleRule => ConditionalFormatDialogFamily.ColorScale,
            IconSetRule => ConditionalFormatDialogFamily.IconSet,
            _ => ConditionalFormatDialogFamily.NewRule,
        };

    public static IReadOnlyList<ConditionalFormatDialogDatePeriodOption> DatePeriodOptions { get; } =
    [
        new("ConditionalFormatDialog_DatePeriod_Yesterday", "yesterday"),
        new("ConditionalFormatDialog_DatePeriod_Today", "today"),
        new("ConditionalFormatDialog_DatePeriod_Tomorrow", "tomorrow"),
        new("ConditionalFormatDialog_DatePeriod_Last7Days", "last7Days"),
        new("ConditionalFormatDialog_DatePeriod_LastWeek", "lastWeek"),
        new("ConditionalFormatDialog_DatePeriod_ThisWeek", "thisWeek"),
        new("ConditionalFormatDialog_DatePeriod_NextWeek", "nextWeek"),
        new("ConditionalFormatDialog_DatePeriod_LastMonth", "lastMonth"),
        new("ConditionalFormatDialog_DatePeriod_ThisMonth", "thisMonth"),
        new("ConditionalFormatDialog_DatePeriod_NextMonth", "nextMonth")
    ];

    public static IReadOnlyList<ConditionalFormatDialogAxisPositionOption> AxisPositionOptions { get; } =
    [
        new("ConditionalFormatDialog_AxisPosition_Automatic", null),
        new("ConditionalFormatDialog_AxisPosition_Middle", "middle"),
        new("ConditionalFormatDialog_AxisPosition_None", "none")
    ];

    public static string FormatStyleKeyForRuleType(string ruleType, bool useThreeColorScale) =>
        ruleType switch
        {
            IconSetRule => "ConditionalFormatDialog_FormatStyle_IconSet",
            ColorScaleRule => useThreeColorScale
                ? "ConditionalFormatDialog_FormatStyle_3ColorScale"
                : "ConditionalFormatDialog_FormatStyle_2ColorScale",
            _ => "ConditionalFormatDialog_FormatStyle_DataBar"
        };

    public static string RuleTypeForFormatStyleKey(string? labelKey) =>
        FindFormatStyle(labelKey)?.RuleType ?? DataBarRule;

    public static bool UseThreeColorScaleForFormatStyleKey(string? labelKey) =>
        FindFormatStyle(labelKey)?.UseThreeColorScale == true;

    public static string DefaultRuleTypeForShellKey(string? shellLabelKey, string currentRuleType)
    {
        if (shellLabelKey == "ConditionalFormatDialog_RuleShell_FormatAllCells")
            return IsVisualRuleType(currentRuleType) ? currentRuleType : DataBarRule;

        return shellLabelKey switch
        {
            "ConditionalFormatDialog_RuleShell_FormatTopBottom" => Top10ItemsRule,
            "ConditionalFormatDialog_RuleShell_FormatAboveBelowAverage" => AboveAverageRule,
            "ConditionalFormatDialog_RuleShell_FormatUniqueDuplicate" => DuplicateValuesRule,
            "ConditionalFormatDialog_RuleShell_UseFormula" => FormulaRule,
            _ => GreaterThanRule
        };
    }

    public static string ShellKeyForRuleType(string ruleType) =>
        ruleType switch
        {
            DataBarRule or ColorScaleRule or IconSetRule => "ConditionalFormatDialog_RuleShell_FormatAllCells",
            Top10ItemsRule or Bottom10ItemsRule or Top10PercentRule or Bottom10PercentRule => "ConditionalFormatDialog_RuleShell_FormatTopBottom",
            AboveAverageRule or BelowAverageRule => "ConditionalFormatDialog_RuleShell_FormatAboveBelowAverage",
            DuplicateValuesRule => "ConditionalFormatDialog_RuleShell_FormatUniqueDuplicate",
            FormulaRule or UseFormulaRule => "ConditionalFormatDialog_RuleShell_UseFormula",
            _ => "ConditionalFormatDialog_RuleShell_FormatContainingCells"
        };

    public static string ConditionKindKeyForRuleType(string ruleType) =>
        ruleType switch
        {
            TextContainsRule or TextDoesNotContainRule or TextBeginsWithRule or TextEndsWithRule => "ConditionalFormatDialog_ConditionKind_SpecificText",
            DateOccurringRule => "ConditionalFormatDialog_ConditionKind_DatesOccurring",
            BlanksRule => "ConditionalFormatDialog_ConditionKind_Blanks",
            NoBlanksRule => "ConditionalFormatDialog_ConditionKind_NoBlanks",
            ErrorsRule => "ConditionalFormatDialog_ConditionKind_Errors",
            NoErrorsRule => "ConditionalFormatDialog_ConditionKind_NoErrors",
            _ => "ConditionalFormatDialog_ConditionKind_CellValue"
        };

    public static string DefaultRuleTypeForConditionKindKey(string? labelKey) =>
        FindOption(ConditionKindOptions, labelKey)?.RuleType ?? GreaterThanRule;

    public static string CellValueOperatorKeyForRuleType(string ruleType) =>
        FindOptionByRuleType(CellValueOperatorOptions, ruleType)?.LabelKey
        ?? "ConditionalFormatDialog_CellValueOperator_GreaterThan";

    public static string SpecificTextOperatorKeyForRuleType(string ruleType) =>
        FindOptionByRuleType(SpecificTextOperatorOptions, ruleType)?.LabelKey
        ?? "ConditionalFormatDialog_TextOperator_Containing";

    public static string DatePeriodValueForKey(string? labelKey) =>
        FindDatePeriod(labelKey)?.Value ?? "today";

    public static string DatePeriodKeyForValue(string? value)
    {
        foreach (var period in DatePeriodOptions)
        {
            if (period.Value == value)
                return period.LabelKey;
        }

        return "ConditionalFormatDialog_DatePeriod_Today";
    }

    public static string? AxisPositionValueForKey(string? labelKey) =>
        FindAxisPosition(labelKey)?.XmlValue;

    public static string AxisPositionKeyForValue(string? xmlValue)
    {
        foreach (var option in AxisPositionOptions)
        {
            if (option.XmlValue == xmlValue)
                return option.LabelKey;
        }

        return "ConditionalFormatDialog_AxisPosition_Automatic";
    }

    public static string? RuleTypeDisplayNameKey(string ruleType) =>
        ruleType switch
        {
            FormulaRule or UseFormulaRule => "ConditionalFormatDialog_RuleType_Formula",
            DataBarRule => "ConditionalFormatDialog_RuleType_DataBar",
            ColorScaleRule => "ConditionalFormatDialog_RuleType_ColorScale",
            IconSetRule => "ConditionalFormatDialog_RuleType_IconSet",
            TextContainsRule => "ConditionalFormatDialog_RuleType_TextContains",
            TextDoesNotContainRule => "ConditionalFormatDialog_RuleType_TextDoesNotContain",
            TextBeginsWithRule => "ConditionalFormatDialog_RuleType_TextBeginsWith",
            TextEndsWithRule => "ConditionalFormatDialog_RuleType_TextEndsWith",
            DateOccurringRule => "ConditionalFormatDialog_RuleType_DateOccurring",
            DuplicateValuesRule => "ConditionalFormatDialog_RuleType_DuplicateValues",
            BlanksRule => "ConditionalFormatDialog_RuleType_Blanks",
            NoBlanksRule => "ConditionalFormatDialog_RuleType_NoBlanks",
            ErrorsRule => "ConditionalFormatDialog_RuleType_Errors",
            NoErrorsRule => "ConditionalFormatDialog_RuleType_NoErrors",
            AboveAverageRule => "ConditionalFormatDialog_RuleType_AboveAverage",
            BelowAverageRule => "ConditionalFormatDialog_RuleType_BelowAverage",
            Top10PercentRule => "ConditionalFormatDialog_RuleType_Top10Percent",
            Bottom10PercentRule => "ConditionalFormatDialog_RuleType_Bottom10Percent",
            Top10ItemsRule => "ConditionalFormatDialog_RuleType_Top10Items",
            Bottom10ItemsRule => "ConditionalFormatDialog_RuleType_Bottom10Items",
            GreaterThanRule => "ConditionalFormatDialog_RuleType_GreaterThan",
            LessThanRule => "ConditionalFormatDialog_RuleType_LessThan",
            EqualToRule => "ConditionalFormatDialog_RuleType_EqualTo",
            BetweenRule => "ConditionalFormatDialog_RuleType_Between",
            NotEqualToRule => "ConditionalFormatDialog_RuleType_NotEqualTo",
            GreaterThanOrEqualToRule => "ConditionalFormatDialog_RuleType_GreaterThanOrEqualTo",
            LessThanOrEqualToRule => "ConditionalFormatDialog_RuleType_LessThanOrEqualTo",
            NotBetweenRule => "ConditionalFormatDialog_RuleType_NotBetween",
            _ => null
        };

    public static bool IsFormulaRuleType(string ruleType) =>
        ruleType is FormulaRule or UseFormulaRule;

    public static bool IsVisualRuleType(string ruleType) =>
        ruleType is DataBarRule or ColorScaleRule or IconSetRule;

    public static bool IsContainsShellRuleType(string ruleType) =>
        ruleType is GreaterThanRule or LessThanRule or EqualToRule or BetweenRule or NotEqualToRule
            or GreaterThanOrEqualToRule or LessThanOrEqualToRule or NotBetweenRule
            or TextContainsRule or TextDoesNotContainRule or TextBeginsWithRule or TextEndsWithRule
            or DateOccurringRule or BlanksRule or NoBlanksRule or ErrorsRule or NoErrorsRule;

    public static bool IsTopBottomRuleType(string ruleType) =>
        ruleType is Top10ItemsRule or Bottom10ItemsRule or Top10PercentRule or Bottom10PercentRule;

    public static bool IsTopRuleType(string ruleType) =>
        ruleType is not (BelowAverageRule or Bottom10ItemsRule or Bottom10PercentRule);

    public static bool IsTopBottomPercentRuleType(string ruleType) =>
        ruleType is Top10PercentRule or Bottom10PercentRule;

    private static ConditionalFormatDialogFormatStyleOption? FindFormatStyle(string? labelKey)
    {
        foreach (var option in FormatStyleOptions)
        {
            if (option.LabelKey == labelKey)
                return option;
        }

        return null;
    }

    private static ConditionalFormatDialogDatePeriodOption? FindDatePeriod(string? labelKey)
    {
        foreach (var period in DatePeriodOptions)
        {
            if (period.LabelKey == labelKey)
                return period;
        }

        return null;
    }

    private static ConditionalFormatDialogAxisPositionOption? FindAxisPosition(string? labelKey)
    {
        foreach (var option in AxisPositionOptions)
        {
            if (option.LabelKey == labelKey)
                return option;
        }

        return null;
    }

    private static ConditionalFormatDialogOption? FindOption(
        IReadOnlyList<ConditionalFormatDialogOption> options,
        string? labelKey)
    {
        foreach (var option in options)
        {
            if (option.LabelKey == labelKey)
                return option;
        }

        return null;
    }

    private static ConditionalFormatDialogOption? FindOptionByRuleType(
        IReadOnlyList<ConditionalFormatDialogOption> options,
        string ruleType)
    {
        foreach (var option in options)
        {
            if (option.RuleType == ruleType)
                return option;
        }

        return null;
    }
}

public static class ConditionalFormatDialogPlanner
{
    public static ConditionalFormat CloneRule(ConditionalFormat source)
        => ManageConditionalFormatsPlanner.CloneWithPriority(source, source.Priority);

    public static CfRuleType ModelRuleTypeForDialogRuleType(
        string ruleType,
        bool uniqueDuplicateValues = false) =>
        ruleType switch
        {
            ConditionalFormatDialogCatalog.DataBarRule => CfRuleType.DataBar,
            ConditionalFormatDialogCatalog.ColorScaleRule => CfRuleType.ColorScale,
            ConditionalFormatDialogCatalog.IconSetRule => CfRuleType.IconSet,
            ConditionalFormatDialogCatalog.TextContainsRule => CfRuleType.ContainsText,
            ConditionalFormatDialogCatalog.TextDoesNotContainRule => CfRuleType.NotContainsText,
            ConditionalFormatDialogCatalog.TextBeginsWithRule => CfRuleType.BeginsWith,
            ConditionalFormatDialogCatalog.TextEndsWithRule => CfRuleType.EndsWith,
            ConditionalFormatDialogCatalog.DateOccurringRule => CfRuleType.DateOccurring,
            ConditionalFormatDialogCatalog.DuplicateValuesRule => uniqueDuplicateValues
                ? CfRuleType.UniqueValues
                : CfRuleType.DuplicateValues,
            ConditionalFormatDialogCatalog.BlanksRule => CfRuleType.Blanks,
            ConditionalFormatDialogCatalog.NoBlanksRule => CfRuleType.NoBlanks,
            ConditionalFormatDialogCatalog.ErrorsRule => CfRuleType.Errors,
            ConditionalFormatDialogCatalog.NoErrorsRule => CfRuleType.NoErrors,
            ConditionalFormatDialogCatalog.AboveAverageRule or ConditionalFormatDialogCatalog.BelowAverageRule => CfRuleType.AboveAverage,
            ConditionalFormatDialogCatalog.Top10ItemsRule
                or ConditionalFormatDialogCatalog.Bottom10ItemsRule
                or ConditionalFormatDialogCatalog.Top10PercentRule
                or ConditionalFormatDialogCatalog.Bottom10PercentRule => CfRuleType.Top10,
            ConditionalFormatDialogCatalog.FormulaRule or ConditionalFormatDialogCatalog.UseFormulaRule => CfRuleType.Formula,
            _ => CfRuleType.CellValue
        };

    public static CfOperator OperatorForDialogRuleType(string ruleType) =>
        ruleType switch
        {
            ConditionalFormatDialogCatalog.GreaterThanRule => CfOperator.GreaterThan,
            ConditionalFormatDialogCatalog.LessThanRule => CfOperator.LessThan,
            ConditionalFormatDialogCatalog.EqualToRule => CfOperator.Equal,
            ConditionalFormatDialogCatalog.BetweenRule => CfOperator.Between,
            ConditionalFormatDialogCatalog.GreaterThanOrEqualToRule => CfOperator.GreaterThanOrEqual,
            ConditionalFormatDialogCatalog.LessThanOrEqualToRule => CfOperator.LessThanOrEqual,
            ConditionalFormatDialogCatalog.NotBetweenRule => CfOperator.NotBetween,
            _ => CfOperator.NotEqual
        };

    public static void ClearNativeConditionalFormatMetadata(ConditionalFormat cf)
    {
        cf.NativeAttributes = null;
        cf.NativeChildXmls = null;
        cf.NativePayloadAttributes = null;
        cf.NativePayloadChildXmls = null;
        cf.NativeContainerAttributes = null;
        cf.NativeContainerChildXmls = null;
    }

    public static string RuleTypeLabel(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula => "Formula",
        CfRuleType.DataBar => "Data Bar",
        CfRuleType.ColorScale => "Color Scale",
        CfRuleType.IconSet => "Icon Set",
        CfRuleType.ContainsText => "Text Contains",
        CfRuleType.NotContainsText => "Text Does Not Contain",
        CfRuleType.BeginsWith => "Text Begins With",
        CfRuleType.EndsWith => "Text Ends With",
        CfRuleType.DateOccurring => "Date Occurring",
        CfRuleType.Blanks => "Blanks",
        CfRuleType.NoBlanks => "No Blanks",
        CfRuleType.Errors => "Errors",
        CfRuleType.NoErrors => "No Errors",
        CfRuleType.DuplicateValues or CfRuleType.UniqueValues => "Duplicate Values",
        CfRuleType.AboveAverage => cf.AboveAverage ? "Above Average" : "Below Average",
        CfRuleType.Top10 => cf.TopBottomPercent
            ? (cf.AboveAverage ? "Top 10%" : "Bottom 10%")
            : (cf.AboveAverage ? "Top 10 Items" : "Bottom 10 Items"),
        CfRuleType.CellValue => cf.Operator switch
        {
            CfOperator.GreaterThan => "Greater Than",
            CfOperator.LessThan => "Less Than",
            CfOperator.Equal => "Equal To",
            CfOperator.Between => "Between",
            CfOperator.NotEqual => "Not Equal To",
            CfOperator.GreaterThanOrEqual => "Greater Than Or Equal To",
            CfOperator.LessThanOrEqual => "Less Than Or Equal To",
            CfOperator.NotBetween => "Not Between",
            _ => "Greater Than"
        },
        _ => "Greater Than"
    };
}
