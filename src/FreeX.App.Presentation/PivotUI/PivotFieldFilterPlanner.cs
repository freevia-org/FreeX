using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

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

    /// <summary>Label-filter kinds in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotLabelFilterKind Kind)> LabelFilterKinds =
    [
        ("Equals", PivotLabelFilterKind.Equals),
        ("Does Not Equal", PivotLabelFilterKind.DoesNotEqual),
        ("Begins With", PivotLabelFilterKind.BeginsWith),
        ("Ends With", PivotLabelFilterKind.EndsWith),
        ("Contains", PivotLabelFilterKind.Contains),
        ("Does Not Contain", PivotLabelFilterKind.DoesNotContain),
        ("Greater Than", PivotLabelFilterKind.GreaterThan),
        ("Greater Than Or Equal To", PivotLabelFilterKind.GreaterThanOrEqual),
        ("Less Than", PivotLabelFilterKind.LessThan),
        ("Less Than Or Equal To", PivotLabelFilterKind.LessThanOrEqual),
        ("Between", PivotLabelFilterKind.Between),
    ];

    /// <summary>Value-filter kinds in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotValueFilterKind Kind)> ValueFilterKinds =
    [
        ("Top", PivotValueFilterKind.Top),
        ("Bottom", PivotValueFilterKind.Bottom),
        ("Greater Than", PivotValueFilterKind.GreaterThan),
        ("Greater Than Or Equal To", PivotValueFilterKind.GreaterThanOrEqual),
        ("Less Than", PivotValueFilterKind.LessThan),
        ("Less Than Or Equal To", PivotValueFilterKind.LessThanOrEqual),
        ("Equals", PivotValueFilterKind.Equals),
        ("Does Not Equal", PivotValueFilterKind.DoesNotEqual),
        ("Between", PivotValueFilterKind.Between),
        ("Not Between", PivotValueFilterKind.NotBetween),
        ("Above Average", PivotValueFilterKind.AboveAverage),
        ("Below Average", PivotValueFilterKind.BelowAverage),
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
        filter = null;
        error = null;

        var first = value1?.Trim() ?? string.Empty;
        if (first.Length == 0)
        {
            error = LabelValueRequiredMessage;
            return false;
        }

        string? second = null;
        if (LabelKindNeedsSecondValue(kind))
        {
            second = value2?.Trim() ?? string.Empty;
            if (second.Length == 0)
            {
                error = LabelSecondValueRequiredMessage;
                return false;
            }
        }

        filter = new PivotLabelFilterModel(sourceFieldIndex, kind, first, second);
        return true;
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
        filter = null;
        error = null;

        var count = 0;
        double? comparison = null;
        double? comparison2 = null;

        if (ValueKindIsTopBottom(kind))
        {
            if (!int.TryParse(primaryText, NumberStyles.Integer, CultureInfo.CurrentCulture, out count) || count <= 0)
            {
                error = PositiveCountRequiredMessage;
                return false;
            }
        }
        else if (!ValueKindIsAverage(kind))
        {
            if (!double.TryParse(primaryText, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed))
            {
                error = NumericValueRequiredMessage;
                return false;
            }

            comparison = parsed;
            if (ValueKindNeedsSecondValue(kind))
            {
                if (!double.TryParse(secondaryText, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed2))
                {
                    error = NumericSecondValueRequiredMessage;
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

    /// <summary>The text that pre-fills the primary value/count box when editing an existing value filter.</summary>
    public static string PrimaryInputText(PivotValueFilterModel? existing)
    {
        if (existing is null)
            return string.Empty;

        return ValueKindIsTopBottom(existing.Kind)
            ? existing.Count.ToString(CultureInfo.CurrentCulture)
            : existing.ComparisonValue?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    /// <summary>The text that pre-fills the second value box when editing an existing value filter.</summary>
    public static string SecondaryInputText(PivotValueFilterModel? existing) =>
        existing?.ComparisonValue2?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

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
