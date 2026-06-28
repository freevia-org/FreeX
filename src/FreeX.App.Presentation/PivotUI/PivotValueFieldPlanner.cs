using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotValueFieldText(string ResourceKey, string FallbackText);

public sealed record PivotValueFieldOption<TValue>(string ResourceKey, string FallbackLabel, TValue Value)
{
    public string Label => FallbackLabel;

    public void Deconstruct(out string label, out TValue value)
    {
        label = Label;
        value = Value;
    }
}

public sealed record PivotValueFieldValidationErrorPlan(
    PivotShowValuesAsValidationError Error,
    string ResourceKey,
    string FallbackMessage);

public enum PivotShowValuesAsValidationError
{
    None,
    MissingBaseField,
    MissingBaseItem
}

/// <summary>
/// Portable, UI-free planning for the PivotTable "Value Field Settings" dialog: the summary-function and
/// show-values-as option catalogs, index resolution, base-field validation, the auto-generated caption
/// rules, and building the resulting <see cref="PivotDataFieldModel"/>. Single-sourced here (English labels)
/// so every desktop host shares identical behavior. Number-format planning is handled separately by the
/// host's number-format parser and is intentionally not part of this portable planner.
/// </summary>
public static class PivotValueFieldPlanner
{
    /// <summary>The "(Automatic)" sentinel label shown at the top of the base-field selector.</summary>
    public static readonly PivotValueFieldText AutomaticBaseField =
        new("PivotValueFieldSettings_AutomaticBaseField", "(Automatic)");

    public static string AutomaticBaseFieldLabel => AutomaticBaseField.FallbackText;

    /// <summary>Summary functions in display order; <c>Value</c> is the OOXML <c>subtotal</c> token.</summary>
    public static readonly IReadOnlyList<PivotValueFieldOption<string>> SummaryFunctions =
    [
        new("PivotValueFieldSettings_SummarySum", "Sum", "sum"),
        new("PivotValueFieldSettings_SummaryCount", "Count", "count"),
        new("PivotValueFieldSettings_SummaryAverage", "Average", "average"),
        new("PivotValueFieldSettings_SummaryMax", "Max", "max"),
        new("PivotValueFieldSettings_SummaryMin", "Min", "min"),
        new("PivotValueFieldSettings_SummaryProduct", "Product", "product"),
        new("PivotValueFieldSettings_SummaryCountNumbers", "Count Numbers", "countNums"),
        new("PivotValueFieldSettings_SummaryStdDev", "StdDev", "stdDev"),
        new("PivotValueFieldSettings_SummaryStdDevp", "StdDevp", "stdDevP"),
        new("PivotValueFieldSettings_SummaryVar", "Var", "var"),
        new("PivotValueFieldSettings_SummaryVarp", "Varp", "varP"),
    ];

    /// <summary>Show-values-as options in display order.</summary>
    public static readonly IReadOnlyList<PivotValueFieldOption<PivotShowValuesAs>> ShowValuesAsOptions =
    [
        new("PivotValueFieldSettings_ShowNoCalculation", "No Calculation", PivotShowValuesAs.None),
        new("PivotValueFieldSettings_ShowPercentOfGrandTotal", "% of Grand Total", PivotShowValuesAs.PercentOfGrandTotal),
        new("PivotValueFieldSettings_ShowPercentOfRowTotal", "% of Row Total", PivotShowValuesAs.PercentOfRowTotal),
        new("PivotValueFieldSettings_ShowPercentOfColumnTotal", "% of Column Total", PivotShowValuesAs.PercentOfColumnTotal),
        new("PivotValueFieldSettings_ShowRunningTotalIn", "Running Total In", PivotShowValuesAs.RunningTotalIn),
        new("PivotValueFieldSettings_ShowDifferenceFrom", "Difference From", PivotShowValuesAs.DifferenceFrom),
        new("PivotValueFieldSettings_ShowPercentDifferenceFrom", "% Difference From", PivotShowValuesAs.PercentDifferenceFrom),
        new("PivotValueFieldSettings_ShowRankSmallest", "Rank Smallest to Largest", PivotShowValuesAs.RankSmallest),
        new("PivotValueFieldSettings_ShowRankLargest", "Rank Largest to Smallest", PivotShowValuesAs.RankLargest),
        new("PivotValueFieldSettings_ShowIndex", "Index", PivotShowValuesAs.Index),
        new("PivotValueFieldSettings_ShowPercentOfParentRowTotal", "% of Parent Row Total", PivotShowValuesAs.PercentOfParentRowTotal),
        new("PivotValueFieldSettings_ShowPercentOfParentColumnTotal", "% of Parent Column Total", PivotShowValuesAs.PercentOfParentColumnTotal),
        new("PivotValueFieldSettings_ShowPercentOfParentTotal", "% of Parent Total", PivotShowValuesAs.PercentOfParentTotal),
    ];

    public static readonly IReadOnlyList<PivotValueFieldValidationErrorPlan> ValidationErrors =
    [
        new(
            PivotShowValuesAsValidationError.MissingBaseField,
            "PivotValueFieldSettings_SelectBaseFieldMessage",
            "Select a base field for the chosen calculation."),
        new(
            PivotShowValuesAsValidationError.MissingBaseItem,
            "PivotValueFieldSettings_EnterBaseItemMessage",
            "Enter a base item for the chosen calculation."),
    ];

    public static int FindSummaryFunctionIndex(string? summaryFunction)
    {
        for (var index = 0; index < SummaryFunctions.Count; index++)
        {
            if (string.Equals(SummaryFunctions[index].Value, summaryFunction, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }

    public static int FindShowValuesAsIndex(PivotShowValuesAs showValuesAs)
    {
        for (var index = 0; index < ShowValuesAsOptions.Count; index++)
        {
            if (ShowValuesAsOptions[index].Value == showValuesAs)
                return index;
        }

        return 0;
    }

    /// <summary>Selected index in the base-field combo: 0 for "(Automatic)", else field index + 1.</summary>
    public static int FindBaseFieldIndex(int? baseFieldIndex, int sourceHeaderCount) =>
        baseFieldIndex is { } index && index >= 0 && index < sourceHeaderCount
            ? index + 1
            : 0;

    public static string SummaryFunctionFromIndex(int selectedIndex) =>
        SummaryFunctions[Math.Max(0, Math.Min(selectedIndex, SummaryFunctions.Count - 1))].Value;

    public static PivotShowValuesAs ShowValuesAsFromIndex(int selectedIndex) =>
        ShowValuesAsOptions[Math.Max(0, Math.Min(selectedIndex, ShowValuesAsOptions.Count - 1))].Value;

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

    public static PivotShowValuesAsValidationError ValidateShowValuesAs(
        PivotShowValuesAs showValuesAs,
        int? baseFieldIndex,
        string? baseItem)
    {
        if (!ShowValuesAsRequiresBaseField(showValuesAs))
            return PivotShowValuesAsValidationError.None;

        if (baseFieldIndex is null)
            return PivotShowValuesAsValidationError.MissingBaseField;

        if (showValuesAs is PivotShowValuesAs.DifferenceFrom or PivotShowValuesAs.PercentDifferenceFrom &&
            string.IsNullOrWhiteSpace(baseItem))
        {
            return PivotShowValuesAsValidationError.MissingBaseItem;
        }

        return PivotShowValuesAsValidationError.None;
    }

    public static PivotValueFieldValidationErrorPlan? DescribeValidationError(
        PivotShowValuesAsValidationError error)
    {
        if (error == PivotShowValuesAsValidationError.None)
            return null;

        for (var index = 0; index < ValidationErrors.Count; index++)
        {
            if (ValidationErrors[index].Error == error)
                return ValidationErrors[index];
        }

        throw new ArgumentOutOfRangeException(nameof(error), error, null);
    }

    public static bool TryValidateShowValuesAs(
        PivotShowValuesAs showValuesAs,
        int? baseFieldIndex,
        string? baseItem,
        out string? error)
    {
        var validationError = ValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem);
        var errorPlan = DescribeValidationError(validationError);
        if (errorPlan is null)
        {
            error = null;
            return true;
        }

        error = errorPlan.FallbackMessage;
        return false;
    }

    /// <summary>Builds the updated data field from the dialog's collected input.</summary>
    public static PivotDataFieldModel CreateResult(
        PivotDataFieldModel initialField,
        IReadOnlyList<string> sourceHeaders,
        string? customName,
        int summaryFunctionIndex,
        int showValuesAsIndex,
        int baseFieldSelectedIndex,
        string? baseItemText)
    {
        ArgumentNullException.ThrowIfNull(initialField);
        ArgumentNullException.ThrowIfNull(sourceHeaders);

        var summaryFunction = SummaryFunctionFromIndex(summaryFunctionIndex);
        var showValuesAs = ShowValuesAsFromIndex(showValuesAsIndex);
        return initialField with
        {
            Name = ResolveResultName(initialField, sourceHeaders, customName, summaryFunction),
            SummaryFunction = summaryFunction,
            ShowValuesAs = showValuesAs,
            BaseFieldIndex = ResolveBaseFieldIndex(showValuesAs, baseFieldSelectedIndex),
            BaseItem = ResolveBaseItem(showValuesAs, baseItemText),
        };
    }

    /// <summary>
    /// Resolves the field's display name: a blank custom name (or one that still matches the
    /// auto-generated caption) regenerates the default "Sum of X" caption for the chosen function.
    /// </summary>
    public static string ResolveResultName(
        PivotDataFieldModel initialField,
        IReadOnlyList<string> sourceHeaders,
        string? customName,
        string summaryFunction)
    {
        var trimmedName = customName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return CreateDefaultCaption(sourceHeaders, initialField.SourceFieldIndex, summaryFunction);

        if (IsAutoGeneratedCaption(initialField, sourceHeaders) &&
            string.Equals(trimmedName, initialField.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return CreateDefaultCaption(sourceHeaders, initialField.SourceFieldIndex, summaryFunction);
        }

        return trimmedName;
    }

    public static bool IsAutoGeneratedCaption(PivotDataFieldModel field, IReadOnlyList<string> sourceHeaders)
    {
        var sourceCaption = SourceFieldCaption(sourceHeaders, field.SourceFieldIndex);
        return SummaryFunctions.Any(function =>
            string.Equals(
                field.Name,
                CreateDefaultCaption(sourceCaption, function.Value),
                StringComparison.CurrentCultureIgnoreCase));
    }

    public static string CreateDefaultCaption(
        IReadOnlyList<string> sourceHeaders,
        int sourceFieldIndex,
        string summaryFunction) =>
        CreateDefaultCaption(SourceFieldCaption(sourceHeaders, sourceFieldIndex), summaryFunction);

    public static string CreateDefaultCaption(string sourceCaption, string summaryFunction)
    {
        var prefix = summaryFunction.ToLowerInvariant() switch
        {
            "count" => "Count",
            "average" => "Average",
            "max" => "Max",
            "min" => "Min",
            "product" => "Product",
            "countnums" => "Count Numbers",
            "stddev" => "StdDev",
            "stddevp" => "StdDevp",
            "var" => "Var",
            "varp" => "Varp",
            _ => "Sum",
        };

        return $"{prefix} of {sourceCaption}";
    }

    private static string SourceFieldCaption(IReadOnlyList<string> sourceHeaders, int sourceFieldIndex) =>
        sourceFieldIndex >= 0 && sourceFieldIndex < sourceHeaders.Count
            ? sourceHeaders[sourceFieldIndex]
            : $"Column {sourceFieldIndex + 1}";
}
