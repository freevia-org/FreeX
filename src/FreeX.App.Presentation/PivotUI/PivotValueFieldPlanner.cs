using System.Globalization;
using Free.Shared.Localization;
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

public sealed record PivotValueNumberFormatPreset(
    string ResourceKey,
    string FallbackLabel,
    int? NumberFormatId,
    string FormatCode)
{
    public string Label => FallbackLabel;
}

public sealed record PivotValueFieldDisplayOption<TValue>(string Label, TValue Value);

public sealed record PivotValueNumberFormatDisplayPreset(
    string Label,
    int? NumberFormatId,
    string FormatCode);

public enum PivotShowValuesAsValidationError
{
    None,
    MissingBaseField,
    MissingBaseItem
}

/// <summary>
/// Portable, UI-free planning for the PivotTable "Value Field Settings" dialog: the summary-function and
/// show-values-as option catalogs, index resolution, base-field validation, the auto-generated caption
/// rules, number-format preset/catalog parsing, and building the resulting <see cref="PivotDataFieldModel"/>.
/// Single-sourced here (English labels) so every desktop host shares identical behavior.
/// </summary>
public static class PivotValueFieldPlanner
{
    public const int DefaultCustomNumberFormatId = 164;

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

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatGeneral", "General", null),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatNumber0Decimals", "Number 0 decimals", 1),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatNumber", "Number", 2),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatComma0Decimals", "Comma 0 decimals", 3),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatNumberWithThousands", "Number with thousands", 4),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCurrency0Decimals", "Currency 0 decimals", 5),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCurrency0DecimalsRedNegatives", "Currency 0 decimals red negatives", 6),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCurrency", "Currency", 7),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCurrencyRedNegatives", "Currency red negatives", 8),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatPercentage0Decimals", "Percentage 0 decimals", 9),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatPercentage", "Percentage", 10),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatScientific", "Scientific", 11),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatFraction", "Fraction", 12),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatFractionTwoDigits", "Fraction two digits", 13),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatShortDate", "Short Date", 14),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatDate", "Date", 14),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatLongDate", "Long Date", 15),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatDayMonth", "Day Month", 16),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatMonthYear", "Month Year", 17),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatTimeAmPm", "Time AM/PM", 18),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatTimeWithSecondsAmPm", "Time with seconds AM/PM", 19),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatTimeHoursMinutes", "Time hours minutes", 20),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatTime", "Time", 21),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatDateTime", "Date Time", 22),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatComma0DecimalsParentheses", "Comma 0 decimals parentheses", 37),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCommaRedNegatives", "Comma red negatives", 38),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCommaParentheses", "Comma parentheses", 39),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatCommaDecimalsRedNegatives", "Comma decimals red negatives", 40),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatAccountingNoSymbol0Decimals", "Accounting no symbol 0 decimals", 41),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatAccounting0Decimals", "Accounting 0 decimals", 42),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatAccountingNoSymbol", "Accounting no symbol", 43),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatAccounting", "Accounting", 44),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatElapsedMinutes", "Elapsed Minutes", 45),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatElapsedTime", "Elapsed Time", 46),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatElapsedMinutesTenths", "Elapsed Minutes Tenths", 47),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatScientificCompact", "Scientific compact", 48),
        NumberFormatPreset("PivotValueFieldSettings_NumberFormatText", "Text", 49),
    ];

    public static string GetAutomaticBaseFieldLabel(ResourceKeyTextResolver text) =>
        text.Get(AutomaticBaseField.ResourceKey);

    public static IReadOnlyList<PivotValueFieldDisplayOption<string>> GetSummaryFunctions(
        ResourceKeyTextResolver text) =>
        SummaryFunctions
            .Select(option => new PivotValueFieldDisplayOption<string>(text.Get(option.ResourceKey), option.Value))
            .ToArray();

    public static IReadOnlyList<PivotValueFieldDisplayOption<PivotShowValuesAs>> GetShowValuesAsOptions(
        ResourceKeyTextResolver text) =>
        ShowValuesAsOptions
            .Select(option => new PivotValueFieldDisplayOption<PivotShowValuesAs>(text.Get(option.ResourceKey), option.Value))
            .ToArray();

    public static IReadOnlyList<PivotValueNumberFormatDisplayPreset> GetNumberFormatPresets(
        ResourceKeyTextResolver text) =>
        NumberFormatPresets
            .Select(preset => new PivotValueNumberFormatDisplayPreset(
                text.Get(preset.ResourceKey),
                preset.NumberFormatId,
                preset.FormatCode))
            .ToArray();

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

    public static int FindNumberFormatPresetIndex(int? numberFormatId)
    {
        for (var index = 0; index < NumberFormatPresets.Count; index++)
        {
            if (NumberFormatPresets[index].NumberFormatId == numberFormatId)
                return index;
        }

        return 0;
    }

    public static int FindNumberFormatPresetIndex(int? numberFormatId, string? formatCode)
    {
        if (!string.IsNullOrWhiteSpace(formatCode))
        {
            for (var index = 0; index < NumberFormatPresets.Count; index++)
            {
                var preset = NumberFormatPresets[index];
                if (preset.NumberFormatId == numberFormatId &&
                    string.Equals(preset.FormatCode, formatCode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        for (var index = 0; index < NumberFormatPresets.Count; index++)
        {
            if (NumberFormatPresets[index].NumberFormatId == numberFormatId)
                return index;
        }

        return -1;
    }

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

    public static bool TryValidateShowValuesAs(
        PivotShowValuesAs showValuesAs,
        int? baseFieldIndex,
        string? baseItem,
        ResourceKeyTextResolver text,
        out string? error)
    {
        var errorPlan = DescribeValidationError(ValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem));
        error = errorPlan is null ? null : text.Get(errorPlan.ResourceKey);
        return errorPlan is null;
    }

    public static bool TryParseOptionalNumberFormatId(string input, out int? numberFormatId)
    {
        numberFormatId = null;
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return true;

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        numberFormatId = parsed;
        return true;
    }

    public static string? ResolveOptionalNumberFormatCode(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static int? ResolveNumberFormatIdForCode(int? numberFormatId, string? numberFormatCode)
    {
        if (string.IsNullOrWhiteSpace(numberFormatCode))
            return numberFormatId;

        return numberFormatId is >= DefaultCustomNumberFormatId
            ? numberFormatId
            : DefaultCustomNumberFormatId;
    }

    public static (int? NumberFormatId, string? NumberFormatCode) ResolveNumberFormatState(string? formatCode)
    {
        var trimmed = formatCode?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, "General", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        if (TryResolveBuiltInNumberFormatIdForCode(trimmed, out var builtInId))
            return (builtInId, null);

        // Format Cells emits the compact positive form for common currency presets, while the
        // OOXML catalog retains its optional alignment padding and negative section. Treat those
        // spellings as the same built-in preset before falling back to a custom format.
        var normalized = NormalizeNumberFormatCode(trimmed);
        foreach (var preset in NumberFormatPresets)
        {
            var presetCode = NormalizeNumberFormatCode(preset.FormatCode);
            if (string.Equals(presetCode, normalized, StringComparison.OrdinalIgnoreCase) ||
                (!trimmed.Contains(';', StringComparison.Ordinal) &&
                 string.Equals(presetCode.Split(';')[0], normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return (preset.NumberFormatId, null);
            }
        }

        return (DefaultCustomNumberFormatId, trimmed);
    }

    public static int? ResolvePresetNumberFormatId(string? label) =>
        string.IsNullOrWhiteSpace(label)
            ? null
            : FindNumberFormatPreset(label.Trim())?.NumberFormatId;

    public static string? ResolvePresetNumberFormatCode(string? label) =>
        string.IsNullOrWhiteSpace(label)
            ? null
            : FindNumberFormatPreset(label.Trim())?.FormatCode;

    public static int? ResolvePresetNumberFormatId(string? label, ResourceKeyTextResolver text) =>
        string.IsNullOrWhiteSpace(label)
            ? null
            : FindNumberFormatDisplayPreset(label.Trim(), text)?.NumberFormatId;

    public static string? ResolvePresetNumberFormatCode(string? label, ResourceKeyTextResolver text) =>
        string.IsNullOrWhiteSpace(label)
            ? null
            : FindNumberFormatDisplayPreset(label.Trim(), text)?.FormatCode;

    public static int? ResolveBuiltInNumberFormatIdForCode(string? formatCode) =>
        TryResolveBuiltInNumberFormatIdForCode(formatCode, out var numberFormatId)
            ? numberFormatId
            : null;

    public static bool TryResolveBuiltInNumberFormatIdForCode(string? formatCode, out int? numberFormatId)
    {
        numberFormatId = null;
        if (string.IsNullOrWhiteSpace(formatCode))
            return false;

        return BuiltInNumberFormatCatalog.TryResolveNumberFormatIdForCode(formatCode, out numberFormatId);
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

    /// <summary>Builds the updated data field including the selected number-format state.</summary>
    public static PivotDataFieldModel CreateResult(
        PivotDataFieldModel initialField,
        IReadOnlyList<string> sourceHeaders,
        string? customName,
        int summaryFunctionIndex,
        int showValuesAsIndex,
        int baseFieldSelectedIndex,
        string? baseItemText,
        int? numberFormatId,
        string? numberFormatCode)
    {
        return CreateResult(
                initialField,
                sourceHeaders,
                customName,
                summaryFunctionIndex,
                showValuesAsIndex,
                baseFieldSelectedIndex,
                baseItemText)
            with
        {
            NumberFormatId = numberFormatId,
            NumberFormatCode = numberFormatCode,
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

    private static PivotValueNumberFormatPreset NumberFormatPreset(
        string resourceKey,
        string fallbackLabel,
        int? numberFormatId) =>
        new(resourceKey, fallbackLabel, numberFormatId, BuiltInFormat(numberFormatId));

    private static PivotValueNumberFormatPreset? FindNumberFormatPreset(string label)
    {
        foreach (var preset in NumberFormatPresets)
        {
            if (string.Equals(preset.Label, label, StringComparison.OrdinalIgnoreCase))
                return preset;
        }

        return null;
    }

    private static PivotValueNumberFormatDisplayPreset? FindNumberFormatDisplayPreset(
        string label,
        ResourceKeyTextResolver text)
    {
        foreach (var preset in GetNumberFormatPresets(text))
        {
            if (string.Equals(preset.Label, label, StringComparison.OrdinalIgnoreCase))
                return preset;
        }

        return null;
    }

    private static string BuiltInFormat(int? numberFormatId) =>
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            ? formatCode
            : "General";

    private static string NormalizeNumberFormatCode(string formatCode) =>
        formatCode.Replace("_)", string.Empty, StringComparison.Ordinal);
}
