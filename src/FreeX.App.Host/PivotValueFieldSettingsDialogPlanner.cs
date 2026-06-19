using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Localized labels + number-format helpers stay here; the index/base-field/show-values-as resolution,
// caption generation, and result building delegate to the portable PivotValueFieldPlanner so the logic is
// single-sourced for the Avalonia/macOS shell. The label arrays below keep the WPF-bound localized strings.
public static class PivotValueFieldSettingsDialogPlanner
{
    public static string AutomaticBaseFieldLabel => UiText.Get("PivotValueFieldSettings_AutomaticBaseField");

    public static readonly (string Label, string Value)[] SummaryFunctions =
    [
        (UiText.Get("PivotValueFieldSettings_SummarySum"), "sum"),
        (UiText.Get("PivotValueFieldSettings_SummaryCount"), "count"),
        (UiText.Get("PivotValueFieldSettings_SummaryAverage"), "average"),
        (UiText.Get("PivotValueFieldSettings_SummaryMax"), "max"),
        (UiText.Get("PivotValueFieldSettings_SummaryMin"), "min"),
        (UiText.Get("PivotValueFieldSettings_SummaryProduct"), "product"),
        (UiText.Get("PivotValueFieldSettings_SummaryCountNumbers"), "countNums"),
        (UiText.Get("PivotValueFieldSettings_SummaryStdDev"), "stdDev"),
        (UiText.Get("PivotValueFieldSettings_SummaryStdDevp"), "stdDevP"),
        (UiText.Get("PivotValueFieldSettings_SummaryVar"), "var"),
        (UiText.Get("PivotValueFieldSettings_SummaryVarp"), "varP")
    ];

    public static readonly (string Label, PivotShowValuesAs Value)[] ShowValuesAsOptions =
    [
        (UiText.Get("PivotValueFieldSettings_ShowNoCalculation"), PivotShowValuesAs.None),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfGrandTotal"), PivotShowValuesAs.PercentOfGrandTotal),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfRowTotal"), PivotShowValuesAs.PercentOfRowTotal),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfColumnTotal"), PivotShowValuesAs.PercentOfColumnTotal),
        (UiText.Get("PivotValueFieldSettings_ShowRunningTotalIn"), PivotShowValuesAs.RunningTotalIn),
        (UiText.Get("PivotValueFieldSettings_ShowDifferenceFrom"), PivotShowValuesAs.DifferenceFrom),
        (UiText.Get("PivotValueFieldSettings_ShowPercentDifferenceFrom"), PivotShowValuesAs.PercentDifferenceFrom),
        (UiText.Get("PivotValueFieldSettings_ShowRankSmallest"), PivotShowValuesAs.RankSmallest),
        (UiText.Get("PivotValueFieldSettings_ShowRankLargest"), PivotShowValuesAs.RankLargest),
        (UiText.Get("PivotValueFieldSettings_ShowIndex"), PivotShowValuesAs.Index),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfParentRowTotal"), PivotShowValuesAs.PercentOfParentRowTotal),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfParentColumnTotal"), PivotShowValuesAs.PercentOfParentColumnTotal),
        (UiText.Get("PivotValueFieldSettings_ShowPercentOfParentTotal"), PivotShowValuesAs.PercentOfParentTotal)
    ];

    public static int FindSummaryFunctionIndex(string? summaryFunction)
    {
        for (var index = 0; index < SummaryFunctions.Length; index++)
        {
            if (string.Equals(SummaryFunctions[index].Value, summaryFunction, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }

    public static int FindShowValuesAsIndex(PivotShowValuesAs showValuesAs)
    {
        for (var index = 0; index < ShowValuesAsOptions.Length; index++)
        {
            if (ShowValuesAsOptions[index].Value == showValuesAs)
                return index;
        }

        return 0;
    }

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
        error = null;
        if (!ShowValuesAsRequiresBaseField(showValuesAs))
            return true;

        if (baseFieldIndex is null)
        {
            error = UiText.Get("PivotValueFieldSettings_SelectBaseFieldMessage");
            return false;
        }

        if (showValuesAs is PivotShowValuesAs.DifferenceFrom or PivotShowValuesAs.PercentDifferenceFrom &&
            string.IsNullOrWhiteSpace(baseItem))
        {
            error = UiText.Get("PivotValueFieldSettings_EnterBaseItemMessage");
            return false;
        }

        return true;
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
        var summaryFunction = SummaryFunctionFromIndex(summaryFunctionIndex);
        var showValuesAs = ShowValuesAsFromIndex(showValuesAsIndex);
        return initialField with
        {
            Name = ResolveResultName(initialField, sourceHeaders ?? [], customName, summaryFunction),
            SummaryFunction = summaryFunction,
            NumberFormatId = numberFormatId,
            NumberFormatCode = numberFormatCode,
            ShowValuesAs = showValuesAs,
            BaseFieldIndex = ResolveBaseFieldIndex(showValuesAs, baseFieldSelectedIndex),
            BaseItem = ResolveBaseItem(showValuesAs, baseItemText)
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
}
