using System.Globalization;

using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotLabelFilterKindOption(string ResourceKey, string FallbackLabel, PivotLabelFilterKind Kind)
{
    public string Label => FallbackLabel;

    public void Deconstruct(out string label, out PivotLabelFilterKind kind)
    {
        label = Label;
        kind = Kind;
    }
}

public sealed record PivotValueFilterKindOption(string ResourceKey, string FallbackLabel, PivotValueFilterKind Kind)
{
    public string Label => FallbackLabel;

    public void Deconstruct(out string label, out PivotValueFilterKind kind)
    {
        label = Label;
        kind = Kind;
    }
}

public sealed record PivotLabelFilterValidationErrorPlan(
    PivotLabelFilterValidationError Error,
    string ResourceKey,
    string FallbackMessage);

public sealed record PivotValueFilterValidationErrorPlan(
    PivotValueFilterValidationError Error,
    string ResourceKey,
    string FallbackMessage);

public enum PivotLabelFilterValidationError
{
    None,
    ValueRequired,
    SecondValueRequired
}

public enum PivotValueFilterValidationError
{
    None,
    PositiveCountRequired,
    NumericValueRequired,
    NumericSecondValueRequired
}

/// <summary>
/// Portable, UI-free planning for the PivotTable field-filter dialog: the label-filter and value-filter kind
/// catalogs (with English display labels), which input boxes each kind needs, validation + parsing of the
/// collected input into a <see cref="PivotLabelFilterModel"/> / <see cref="PivotValueFilterModel"/>, and the
/// per-field replacement helpers that produce the full filter lists to hand to the pivot mutation command.
/// Also resolves a checklist of allowed member values (the manual item filter) from a field's current
/// selection. Single-sourced here (English labels) so every desktop host shares identical behavior; the host
/// keeps only the widget construction and the command execution.
/// </summary>
public static class PivotFieldFilterPlanner
{
    public const string ValueFilterRequiresValueFieldMessage =
        "Add a PivotTable value field before applying a value filter.";
    public const string LabelValueRequiredMessage =
        "Enter a value for the label filter.";
    public const string LabelSecondValueRequiredMessage =
        "Enter both values for a Between label filter.";
    public const string PositiveCountRequiredMessage =
        "Enter a positive item count for a Top/Bottom filter.";
    public const string NumericValueRequiredMessage =
        "Enter a numeric comparison value.";
    public const string NumericSecondValueRequiredMessage =
        "Enter a numeric second value for a Between filter.";
    public const PivotLabelFilterKind DefaultLabelFilterKind = PivotLabelFilterKind.Contains;
    public const PivotValueFilterKind DefaultValueFilterKind = PivotValueFilterKind.GreaterThan;
    public const string DefaultValueFilterPrimaryText = "0";
    public const string ValueFilterComparisonDisplayFormat = "0.########";

    /// <summary>Label-filter kinds in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<PivotLabelFilterKindOption> LabelFilterKinds =
    [
        new("PivotLabelFilter_Equals", "Equals", PivotLabelFilterKind.Equals),
        new("PivotLabelFilter_DoesNotEqual", "Does Not Equal", PivotLabelFilterKind.DoesNotEqual),
        new("PivotLabelFilter_BeginsWith", "Begins With", PivotLabelFilterKind.BeginsWith),
        new("PivotLabelFilter_EndsWith", "Ends With", PivotLabelFilterKind.EndsWith),
        new("PivotLabelFilter_Contains", "Contains", PivotLabelFilterKind.Contains),
        new("PivotLabelFilter_DoesNotContain", "Does Not Contain", PivotLabelFilterKind.DoesNotContain),
        new("PivotLabelFilter_GreaterThan", "Greater Than", PivotLabelFilterKind.GreaterThan),
        new("PivotLabelFilter_GreaterThanOrEqualTo", "Greater Than Or Equal To", PivotLabelFilterKind.GreaterThanOrEqual),
        new("PivotLabelFilter_LessThan", "Less Than", PivotLabelFilterKind.LessThan),
        new("PivotLabelFilter_LessThanOrEqualTo", "Less Than Or Equal To", PivotLabelFilterKind.LessThanOrEqual),
        new("PivotLabelFilter_Between", "Between", PivotLabelFilterKind.Between),
    ];

    /// <summary>Value-filter kinds in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<PivotValueFilterKindOption> ValueFilterKinds =
    [
        new("PivotValueFilter_Top", "Top", PivotValueFilterKind.Top),
        new("PivotValueFilter_Bottom", "Bottom", PivotValueFilterKind.Bottom),
        new("PivotValueFilter_GreaterThan", "Greater Than", PivotValueFilterKind.GreaterThan),
        new("PivotValueFilter_GreaterThanOrEqual", "Greater Than Or Equal To", PivotValueFilterKind.GreaterThanOrEqual),
        new("PivotValueFilter_LessThan", "Less Than", PivotValueFilterKind.LessThan),
        new("PivotValueFilter_LessThanOrEqual", "Less Than Or Equal To", PivotValueFilterKind.LessThanOrEqual),
        new("PivotValueFilter_Equals", "Equals", PivotValueFilterKind.Equals),
        new("PivotValueFilter_DoesNotEqual", "Does Not Equal", PivotValueFilterKind.DoesNotEqual),
        new("PivotValueFilter_Between", "Between", PivotValueFilterKind.Between),
        new("PivotValueFilter_NotBetween", "Not Between", PivotValueFilterKind.NotBetween),
        new("PivotValueFilter_AboveAverage", "Above Average", PivotValueFilterKind.AboveAverage),
        new("PivotValueFilter_BelowAverage", "Below Average", PivotValueFilterKind.BelowAverage),
    ];

    public static readonly IReadOnlyList<PivotLabelFilterValidationErrorPlan> LabelFilterValidationErrors =
    [
        new(
            PivotLabelFilterValidationError.ValueRequired,
            "PivotLabelFilter_ValueRequiredMessage",
            LabelValueRequiredMessage),
        new(
            PivotLabelFilterValidationError.SecondValueRequired,
            "PivotLabelFilter_EndingValueRequiredMessage",
            LabelSecondValueRequiredMessage),
    ];

    public static readonly IReadOnlyList<PivotValueFilterValidationErrorPlan> ValueFilterValidationErrors =
    [
        new(
            PivotValueFilterValidationError.PositiveCountRequired,
            "PivotValueFilter_PositiveItemCountMessage",
            PositiveCountRequiredMessage),
        new(
            PivotValueFilterValidationError.NumericValueRequired,
            "PivotValueFilter_NumericComparisonMessage",
            NumericValueRequiredMessage),
        new(
            PivotValueFilterValidationError.NumericSecondValueRequired,
            "PivotValueFilter_NumericEndingComparisonMessage",
            NumericSecondValueRequiredMessage),
    ];

    public static int FindLabelKindIndex(PivotLabelFilterKind kind)
    {
        for (var index = 0; index < LabelFilterKinds.Count; index++)
        {
            if (LabelFilterKinds[index].Kind == kind)
                return index;
        }

        return 0;
    }

    public static int FindValueKindIndex(PivotValueFilterKind kind)
    {
        for (var index = 0; index < ValueFilterKinds.Count; index++)
        {
            if (ValueFilterKinds[index].Kind == kind)
                return index;
        }

        return 0;
    }

    public static PivotLabelFilterKind LabelKindFromIndex(int selectedIndex) =>
        LabelFilterKinds[Math.Max(0, Math.Min(selectedIndex, LabelFilterKinds.Count - 1))].Kind;

    public static PivotValueFilterKind ValueKindFromIndex(int selectedIndex) =>
        ValueFilterKinds[Math.Max(0, Math.Min(selectedIndex, ValueFilterKinds.Count - 1))].Kind;

    public static int DefaultValueKindIndex => FindValueKindIndex(DefaultValueFilterKind);

    public static int DefaultLabelKindIndex => FindLabelKindIndex(DefaultLabelFilterKind);

    /// <summary>True when the label-filter kind needs a second value box (Between).</summary>
    public static bool LabelKindNeedsSecondValue(PivotLabelFilterKind kind) =>
        kind == PivotLabelFilterKind.Between;

    /// <summary>True when the value-filter kind reads a Top/Bottom item count rather than a comparison value.</summary>
    public static bool ValueKindIsTopBottom(PivotValueFilterKind kind) =>
        kind is PivotValueFilterKind.Top or PivotValueFilterKind.Bottom;

    /// <summary>True when the value-filter kind reads no numeric input at all (the average comparisons).</summary>
    public static bool ValueKindIsAverage(PivotValueFilterKind kind) =>
        kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage;

    /// <summary>True when the value-filter kind needs a second comparison value (Between / Not Between).</summary>
    public static bool ValueKindNeedsSecondValue(PivotValueFilterKind kind) =>
        kind is PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween;

    /// <summary>True when the value-filter kind needs the primary numeric/count box visible.</summary>
    public static bool ValueKindNeedsPrimaryInput(PivotValueFilterKind kind) =>
        !ValueKindIsAverage(kind);

    /// <summary>
    /// Validates + builds a label filter from the dialog's collected input. Returns false (with
    /// <paramref name="error"/> set) when a required value is missing.
    /// </summary>
    public static bool TryCreateLabelFilter(
        int sourceFieldIndex,
        PivotLabelFilterKind kind,
        string? value1,
        string? value2,
        out PivotLabelFilterModel? filter,
        out string? error)
    {
        var result = TryCreateLabelFilterWithValidationError(sourceFieldIndex, kind, value1, value2, out filter, out var validationError);
        error = DescribeLabelFilterValidationError(validationError)?.FallbackMessage;
        return result;
    }

    public static bool TryCreateLabelFilterWithValidationError(
        int sourceFieldIndex,
        PivotLabelFilterKind kind,
        string? value1,
        string? value2,
        out PivotLabelFilterModel? filter,
        out PivotLabelFilterValidationError error)
    {
        filter = null;
        error = PivotLabelFilterValidationError.None;

        var first = value1?.Trim() ?? string.Empty;
        if (first.Length == 0)
        {
            error = PivotLabelFilterValidationError.ValueRequired;
            return false;
        }

        string? second = null;
        if (LabelKindNeedsSecondValue(kind))
        {
            second = value2?.Trim() ?? string.Empty;
            if (second.Length == 0)
            {
                error = PivotLabelFilterValidationError.SecondValueRequired;
                return false;
            }
        }

        filter = new PivotLabelFilterModel(sourceFieldIndex, kind, first, second);
        return true;
    }

    public static PivotLabelFilterValidationErrorPlan? DescribeLabelFilterValidationError(
        PivotLabelFilterValidationError error)
    {
        if (error == PivotLabelFilterValidationError.None)
            return null;

        for (var index = 0; index < LabelFilterValidationErrors.Count; index++)
        {
            if (LabelFilterValidationErrors[index].Error == error)
                return LabelFilterValidationErrors[index];
        }

        throw new ArgumentOutOfRangeException(nameof(error), error, null);
    }

    /// <summary>
    /// Validates + builds a value filter from the dialog's collected input. Parses the Top/Bottom count or the
    /// numeric comparison value(s) per the chosen kind. Returns false (with <paramref name="error"/> set) on a
    /// missing/invalid number.
    /// </summary>
    public static bool TryCreateValueFilter(
        int sourceFieldIndex,
        int dataFieldIndex,
        PivotValueFilterKind kind,
        string? primaryText,
        string? secondaryText,
        out PivotValueFilterModel? filter,
        out string? error)
    {
        var result = TryCreateValueFilter(
            sourceFieldIndex,
            dataFieldIndex,
            kind,
            primaryText,
            secondaryText,
            CultureInfo.CurrentCulture,
            out filter,
            out var validationError);
        error = DescribeValueFilterValidationError(validationError)?.FallbackMessage;
        return result;
    }

    public static bool TryCreateValueFilter(
        int sourceFieldIndex,
        int dataFieldIndex,
        PivotValueFilterKind kind,
        string? primaryText,
        string? secondaryText,
        CultureInfo culture,
        out PivotValueFilterModel? filter,
        out PivotValueFilterValidationError error)
    {
        ArgumentNullException.ThrowIfNull(culture);

        filter = null;
        error = PivotValueFilterValidationError.None;

        var count = 0;
        double? comparison = null;
        double? comparison2 = null;

        if (ValueKindIsTopBottom(kind))
        {
            if (!int.TryParse(primaryText?.Trim() ?? string.Empty, NumberStyles.Integer, culture, out count) || count <= 0)
            {
                error = PivotValueFilterValidationError.PositiveCountRequired;
                return false;
            }
        }
        else if (!ValueKindIsAverage(kind))
        {
            // Try the caller's culture first, then fall back to invariant - the same
            // convention every other numeric-entry parser in the app uses (ChartDialogValueParser,
            // DrawingInputParser, PageSetupDialogModel, etc). NumberStyles.Any (which implies
            // AllowThousands) is deliberately avoided: on a comma-decimal culture (e.g. de-DE,
            // where '.' is the group separator) it silently reinterprets a period-decimal value
            // like "1000.5" as the grouped integer 10005 instead of failing or parsing 1000.5.
            if (!NumericInputParser.TryParseFiniteDouble(
                    primaryText ?? string.Empty,
                    culture,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                error = PivotValueFilterValidationError.NumericValueRequired;
                return false;
            }

            comparison = parsed;
            if (ValueKindNeedsSecondValue(kind))
            {
                if (!NumericInputParser.TryParseFiniteDouble(
                        secondaryText ?? string.Empty,
                        culture,
                        CultureInfo.InvariantCulture,
                        out var parsed2))
                {
                    error = PivotValueFilterValidationError.NumericSecondValueRequired;
                    return false;
                }

                comparison2 = parsed2;
            }
        }

        filter = new PivotValueFilterModel(
            Math.Max(0, dataFieldIndex),
            kind,
            count,
            comparison,
            comparison2,
            sourceFieldIndex);
        return true;
    }

    public static PivotValueFilterValidationErrorPlan? DescribeValueFilterValidationError(
        PivotValueFilterValidationError error)
    {
        if (error == PivotValueFilterValidationError.None)
            return null;

        for (var index = 0; index < ValueFilterValidationErrors.Count; index++)
        {
            if (ValueFilterValidationErrors[index].Error == error)
                return ValueFilterValidationErrors[index];
        }

        throw new ArgumentOutOfRangeException(nameof(error), error, null);
    }

    /// <summary>The text that pre-fills the primary value/count box when editing an existing value filter.</summary>
    public static string PrimaryInputText(PivotValueFilterModel? existing) =>
        PrimaryInputText(existing, CultureInfo.CurrentCulture);

    public static string PrimaryInputText(PivotValueFilterModel? existing, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (existing is null)
            return string.Empty;

        return ValueKindIsTopBottom(existing.Kind)
            ? existing.Count.ToString(culture)
            : existing.ComparisonValue?.ToString(ValueFilterComparisonDisplayFormat, culture) ?? string.Empty;
    }

    /// <summary>The text that pre-fills the second value box when editing an existing value filter.</summary>
    public static string SecondaryInputText(PivotValueFilterModel? existing) =>
        SecondaryInputText(existing, CultureInfo.CurrentCulture);

    public static string SecondaryInputText(PivotValueFilterModel? existing, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return existing?.ComparisonValue2?.ToString(ValueFilterComparisonDisplayFormat, culture) ?? string.Empty;
    }

    /// <summary>The data-field combo selection for editing a value filter, clamped to the data-field range.</summary>
    public static int InitialDataFieldIndex(PivotValueFilterModel? existing, int dataFieldCount)
    {
        if (dataFieldCount <= 0)
            return -1;

        if (existing is { } filter && filter.DataFieldIndex >= 0 && filter.DataFieldIndex < dataFieldCount)
            return filter.DataFieldIndex;

        return 0;
    }

    /// <summary>The full label-filter list with the field's existing filter replaced (or removed when null).</summary>
    public static IReadOnlyList<PivotLabelFilterModel> ReplaceFieldLabelFilter(
        IReadOnlyList<PivotLabelFilterModel> existing,
        int sourceFieldIndex,
        PivotLabelFilterModel? filter)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var result = existing
            .Where(item => item.SourceFieldIndex != sourceFieldIndex)
            .ToList();
        if (filter is not null)
            result.Add(filter);

        return result;
    }

    /// <summary>The full value-filter list with the field's existing filter replaced (or removed when null).</summary>
    public static IReadOnlyList<PivotValueFilterModel> ReplaceFieldValueFilter(
        IReadOnlyList<PivotValueFilterModel> existing,
        int sourceFieldIndex,
        PivotValueFilterModel? filter)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var result = existing
            .Where(item => item.SourceFieldIndex != sourceFieldIndex)
            .ToList();
        if (filter is not null)
            result.Add(filter);

        return result;
    }

    /// <summary>
    /// Resolves the set of currently-allowed member values for the checklist from a field's current selection.
    /// A null/empty selection (or the "(All)" sentinel) means every member is allowed, so this returns null.
    /// </summary>
    public static IReadOnlyCollection<string>? ResolveAllowedItems(IReadOnlyList<string>? currentSelection)
    {
        if (currentSelection is not { Count: > 0 })
            return null;

        var allowed = currentSelection
            .Where(item => !string.IsNullOrWhiteSpace(item) &&
                           !string.Equals(item, "(All)", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return allowed.Count == 0 ? null : allowed;
    }

    public static bool IsFilterItemVisible(string item, string? query) =>
        string.IsNullOrWhiteSpace(query) ||
        item.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);

    public static bool? ResolveSelectAllState(IReadOnlyList<bool> visibleChecked)
    {
        ArgumentNullException.ThrowIfNull(visibleChecked);
        return visibleChecked.Count switch
        {
            0 => false,
            _ when visibleChecked.All(isChecked => isChecked) => true,
            _ when visibleChecked.All(isChecked => !isChecked) => false,
            _ => null,
        };
    }

    /// <summary>
    /// Maps the checklist's checked members back to a field selection: selecting every member is "no filter"
    /// (returns null so newly-arriving members stay visible); otherwise returns the checked members.
    /// </summary>
    public static IReadOnlyList<string>? ResolveItemSelection(
        IReadOnlyList<string> checkedMembers,
        int totalMemberCount)
    {
        ArgumentNullException.ThrowIfNull(checkedMembers);
        return checkedMembers.Count >= totalMemberCount ? null : checkedMembers;
    }
}
