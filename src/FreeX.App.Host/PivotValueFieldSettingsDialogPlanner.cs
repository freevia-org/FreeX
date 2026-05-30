using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        var index = Array.FindIndex(
            SummaryFunctions,
            item => string.Equals(item.Value, summaryFunction, StringComparison.OrdinalIgnoreCase));
        return Math.Max(0, index);
    }

    public static int FindShowValuesAsIndex(PivotShowValuesAs showValuesAs)
    {
        var index = Array.FindIndex(ShowValuesAsOptions, item => item.Value == showValuesAs);
        return Math.Max(0, index);
    }

    public static int FindBaseFieldIndex(int? baseFieldIndex, int sourceHeaderCount) =>
        baseFieldIndex is { } index && index >= 0 && index < sourceHeaderCount
            ? index + 1
            : 0;

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
        SummaryFunctions[Math.Max(0, Math.Min(selectedIndex, SummaryFunctions.Length - 1))].Value;

    public static PivotShowValuesAs ShowValuesAsFromIndex(int selectedIndex) =>
        ShowValuesAsOptions[Math.Max(0, Math.Min(selectedIndex, ShowValuesAsOptions.Length - 1))].Value;

    public static bool ShowValuesAsRequiresBaseField(PivotShowValuesAs showValuesAs) =>
        showValuesAs is PivotShowValuesAs.RunningTotalIn
            or PivotShowValuesAs.DifferenceFrom
            or PivotShowValuesAs.PercentDifferenceFrom
            or PivotShowValuesAs.RankSmallest
            or PivotShowValuesAs.RankLargest
            or PivotShowValuesAs.PercentOfParentTotal;

    public static int? ResolveBaseFieldIndex(PivotShowValuesAs showValuesAs, int selectedIndex) =>
        ShowValuesAsRequiresBaseField(showValuesAs) && selectedIndex > 0
            ? selectedIndex - 1
            : null;

    public static string? ResolveBaseItem(PivotShowValuesAs showValuesAs, string? text) =>
        !ShowValuesAsRequiresBaseField(showValuesAs) || string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim();

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
        string? customName,
        int summaryFunctionIndex,
        int showValuesAsIndex,
        int baseFieldSelectedIndex,
        string? baseItemText,
        int? numberFormatId,
        string? numberFormatCode)
    {
        var showValuesAs = ShowValuesAsFromIndex(showValuesAsIndex);
        return initialField with
        {
            Name = string.IsNullOrWhiteSpace(customName) ? initialField.Name : customName.Trim(),
            SummaryFunction = SummaryFunctionFromIndex(summaryFunctionIndex),
            NumberFormatId = numberFormatId,
            NumberFormatCode = numberFormatCode,
            ShowValuesAs = showValuesAs,
            BaseFieldIndex = ResolveBaseFieldIndex(showValuesAs, baseFieldSelectedIndex),
            BaseItem = ResolveBaseItem(showValuesAs, baseItemText)
        };
    }
}
