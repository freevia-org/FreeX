using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class PivotValueFieldSettingsDialogPlanner
{
    public const string AutomaticBaseFieldLabel = "(Automatic)";

    public static readonly (string Label, string Value)[] SummaryFunctions =
    [
        ("Sum", "sum"),
        ("Count", "count"),
        ("Average", "average"),
        ("Max", "max"),
        ("Min", "min"),
        ("Product", "product"),
        ("Count Numbers", "countNums"),
        ("StdDev", "stdDev"),
        ("StdDevp", "stdDevP"),
        ("Var", "var"),
        ("Varp", "varP")
    ];

    public static readonly (string Label, PivotShowValuesAs Value)[] ShowValuesAsOptions =
    [
        ("No Calculation", PivotShowValuesAs.None),
        ("% of Grand Total", PivotShowValuesAs.PercentOfGrandTotal),
        ("% of Row Total", PivotShowValuesAs.PercentOfRowTotal),
        ("% of Column Total", PivotShowValuesAs.PercentOfColumnTotal),
        ("Running Total In", PivotShowValuesAs.RunningTotalIn),
        ("Difference From", PivotShowValuesAs.DifferenceFrom),
        ("% Difference From", PivotShowValuesAs.PercentDifferenceFrom),
        ("Rank Smallest to Largest", PivotShowValuesAs.RankSmallest),
        ("Rank Largest to Smallest", PivotShowValuesAs.RankLargest),
        ("Index", PivotShowValuesAs.Index),
        ("% of Parent Row Total", PivotShowValuesAs.PercentOfParentRowTotal),
        ("% of Parent Column Total", PivotShowValuesAs.PercentOfParentColumnTotal),
        ("% of Parent Total", PivotShowValuesAs.PercentOfParentTotal)
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
            error = "Select a base field for this Show Values As calculation.";
            return false;
        }

        if (showValuesAs is PivotShowValuesAs.DifferenceFrom or PivotShowValuesAs.PercentDifferenceFrom &&
            string.IsNullOrWhiteSpace(baseItem))
        {
            error = "Enter a base item for this Show Values As calculation.";
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
