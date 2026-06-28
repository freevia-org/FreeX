using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Localized labels, localized validation text, and number-format helpers stay here. Option order,
// index/base-field/show-values-as resolution, caption generation, and result building delegate to the
// portable PivotValueFieldPlanner so the logic is single-sourced for Avalonia/macOS.
public static class PivotValueFieldSettingsDialogPlanner
{
    public static string AutomaticBaseFieldLabel => UiText.Get("PivotValueFieldSettings_AutomaticBaseField");

    private static readonly string[] SummaryFunctionResourceKeys =
    [
        "PivotValueFieldSettings_SummarySum",
        "PivotValueFieldSettings_SummaryCount",
        "PivotValueFieldSettings_SummaryAverage",
        "PivotValueFieldSettings_SummaryMax",
        "PivotValueFieldSettings_SummaryMin",
        "PivotValueFieldSettings_SummaryProduct",
        "PivotValueFieldSettings_SummaryCountNumbers",
        "PivotValueFieldSettings_SummaryStdDev",
        "PivotValueFieldSettings_SummaryStdDevp",
        "PivotValueFieldSettings_SummaryVar",
        "PivotValueFieldSettings_SummaryVarp"
    ];

    private static readonly string[] ShowValuesAsResourceKeys =
    [
        "PivotValueFieldSettings_ShowNoCalculation",
        "PivotValueFieldSettings_ShowPercentOfGrandTotal",
        "PivotValueFieldSettings_ShowPercentOfRowTotal",
        "PivotValueFieldSettings_ShowPercentOfColumnTotal",
        "PivotValueFieldSettings_ShowRunningTotalIn",
        "PivotValueFieldSettings_ShowDifferenceFrom",
        "PivotValueFieldSettings_ShowPercentDifferenceFrom",
        "PivotValueFieldSettings_ShowRankSmallest",
        "PivotValueFieldSettings_ShowRankLargest",
        "PivotValueFieldSettings_ShowIndex",
        "PivotValueFieldSettings_ShowPercentOfParentRowTotal",
        "PivotValueFieldSettings_ShowPercentOfParentColumnTotal",
        "PivotValueFieldSettings_ShowPercentOfParentTotal"
    ];

    public static readonly (string Label, string Value)[] SummaryFunctions =
        LocalizeOptions(PivotValueFieldPlanner.SummaryFunctions, SummaryFunctionResourceKeys);

    public static readonly (string Label, PivotShowValuesAs Value)[] ShowValuesAsOptions =
        LocalizeOptions(PivotValueFieldPlanner.ShowValuesAsOptions, ShowValuesAsResourceKeys);

    public static int FindSummaryFunctionIndex(string? summaryFunction) =>
        PivotValueFieldPlanner.FindSummaryFunctionIndex(summaryFunction);

    public static int FindShowValuesAsIndex(PivotShowValuesAs showValuesAs) =>
        PivotValueFieldPlanner.FindShowValuesAsIndex(showValuesAs);

    public static int FindBaseFieldIndex(int? baseFieldIndex, int sourceHeaderCount) =>
        PivotValueFieldPlanner.FindBaseFieldIndex(baseFieldIndex, sourceHeaderCount);

    public static int FindNumberFormatPresetIndex(int? numberFormatId)
    {
        var presets = PivotValueFieldSettingsInputParser.NumberFormatPresets;
        for (var index = 0; index < presets.Count; index++)
        {
            if (presets[index].NumberFormatId == numberFormatId)
                return index;
        }

        return 0;
    }

    public static string SummaryFunctionFromIndex(int selectedIndex) =>
        PivotValueFieldPlanner.SummaryFunctionFromIndex(selectedIndex);

    public static PivotShowValuesAs ShowValuesAsFromIndex(int selectedIndex) =>
        PivotValueFieldPlanner.ShowValuesAsFromIndex(selectedIndex);

    public static bool ShowValuesAsRequiresBaseField(PivotShowValuesAs showValuesAs) =>
        PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(showValuesAs);

    public static int? ResolveBaseFieldIndex(PivotShowValuesAs showValuesAs, int selectedIndex) =>
        PivotValueFieldPlanner.ResolveBaseFieldIndex(showValuesAs, selectedIndex);

    public static string? ResolveBaseItem(PivotShowValuesAs showValuesAs, string? text) =>
        PivotValueFieldPlanner.ResolveBaseItem(showValuesAs, text);

    public static bool TryValidateShowValuesAs(
        PivotShowValuesAs showValuesAs,
        int? baseFieldIndex,
        string? baseItem,
        out string? error)
    {
        switch (PivotValueFieldPlanner.ValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem))
        {
            case PivotShowValuesAsValidationError.None:
                error = null;
                return true;
            case PivotShowValuesAsValidationError.MissingBaseField:
                error = UiText.Get("PivotValueFieldSettings_SelectBaseFieldMessage");
                return false;
            case PivotShowValuesAsValidationError.MissingBaseItem:
                error = UiText.Get("PivotValueFieldSettings_EnterBaseItemMessage");
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(showValuesAs));
        }
    }

    public static PivotDataFieldModel CreateResult(
        PivotDataFieldModel initialField,
        IReadOnlyList<string>? sourceHeaders,
        string? customName,
        int summaryFunctionIndex,
        int showValuesAsIndex,
        int baseFieldSelectedIndex,
        string? baseItemText,
        int? numberFormatId,
        string? numberFormatCode)
    {
        var result = PivotValueFieldPlanner.CreateResult(
            initialField,
            sourceHeaders ?? [],
            customName,
            summaryFunctionIndex,
            showValuesAsIndex,
            baseFieldSelectedIndex,
            baseItemText);

        return result with
        {
            NumberFormatId = numberFormatId,
            NumberFormatCode = numberFormatCode
        };
    }

    public static PivotDataFieldModel CreateResult(
        PivotDataFieldModel initialField,
        string? customName,
        int summaryFunctionIndex,
        int showValuesAsIndex,
        int baseFieldSelectedIndex,
        string? baseItemText,
        int? numberFormatId,
        string? numberFormatCode) =>
        CreateResult(
            initialField,
            sourceHeaders: [],
            customName,
            summaryFunctionIndex,
            showValuesAsIndex,
            baseFieldSelectedIndex,
            baseItemText,
            numberFormatId,
            numberFormatCode);

    public static string ResolveResultName(
        PivotDataFieldModel initialField,
        IReadOnlyList<string> sourceHeaders,
        string? customName,
        string summaryFunction) =>
        PivotValueFieldPlanner.ResolveResultName(initialField, sourceHeaders, customName, summaryFunction);

    public static bool IsAutoGeneratedCaption(PivotDataFieldModel field, IReadOnlyList<string> sourceHeaders) =>
        PivotValueFieldPlanner.IsAutoGeneratedCaption(field, sourceHeaders);

    public static string CreateDefaultCaption(
        IReadOnlyList<string> sourceHeaders,
        int sourceFieldIndex,
        string summaryFunction) =>
        PivotValueFieldPlanner.CreateDefaultCaption(sourceHeaders, sourceFieldIndex, summaryFunction);

    public static string CreateDefaultCaption(string sourceCaption, string summaryFunction) =>
        PivotValueFieldPlanner.CreateDefaultCaption(sourceCaption, summaryFunction);

    private static (string Label, TValue Value)[] LocalizeOptions<TValue>(
        IReadOnlyList<(string Label, TValue Value)> sharedOptions,
        IReadOnlyList<string> resourceKeys)
    {
        if (sharedOptions.Count != resourceKeys.Count)
            throw new InvalidOperationException("Pivot value-field localized option catalogs must match the shared catalog order.");

        return sharedOptions
            .Select((option, index) => (UiText.Get(resourceKeys[index]), option.Value))
            .ToArray();
    }
}
