using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable carrier for the layout field lists a grouping change rewrites (a grouped/ungrouped row, column,
/// or page field is replaced in place, or appended to the row area when it is not yet in the layout).
/// </summary>
public sealed record PivotGroupFieldLayout(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields);

public sealed record PivotGroupFieldSubmission(
    string SourceFieldName,
    PivotFieldModel Field,
    bool Ungroup);

/// <summary>
/// Portable, UI-free planning for the "Group Field" / "Ungroup" PivotTable dialogs: the group-by catalog
/// (None / Year / Quarter / Month / Day / Number range), capturing the current grouping off a source field,
/// validating the starting/ending/by inputs, building the resulting grouped (or ungrouped)
/// <see cref="PivotFieldModel"/>, and rewriting the row/column/page layout lists to carry it. Single-sourced
/// here so the desktop host and the cross-platform shell share identical behavior; building the dialog and
/// running the command stays with each shell's command glue (both hand the layout to
/// <c>ConfigurePivotTableCalculatedItemsCommand</c>, which carries the row/column/page fields plus the
/// existing calculated fields/items). Mirrors the Core <see cref="PivotFieldGrouping"/> model exactly, so no
/// Core change is required.
/// </summary>
public static class PivotGroupFieldPlanner
{
    public const string InvalidStartMessage = "Enter a valid starting value (a number, or leave it blank for automatic).";

    public const string InvalidEndMessage = "Enter a valid ending value (a number, or leave it blank for automatic).";

    public const string InvalidIntervalMessage = "Enter a positive number of values to group by.";

    /// <summary>Group-by options in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotFieldGrouping Value)> Groupings =
    [
        ("(Do not group)", PivotFieldGrouping.None),
        ("Years", PivotFieldGrouping.Year),
        ("Quarters", PivotFieldGrouping.Quarter),
        ("Months", PivotFieldGrouping.Month),
        ("Days", PivotFieldGrouping.Day),
        ("Number range", PivotFieldGrouping.NumberRange),
    ];

    public static int FindGroupingIndex(PivotFieldGrouping grouping)
    {
        for (var index = 0; index < Groupings.Count; index++)
        {
            if (Groupings[index].Value == grouping)
                return index;
        }

        return 0;
    }

    public static PivotFieldGrouping GroupingFromIndex(int selectedIndex) =>
        Groupings[Math.Max(0, Math.Min(selectedIndex, Groupings.Count - 1))].Value;

    /// <summary>True when the group-by selection needs the starting/ending/by numeric range inputs.</summary>
    public static bool GroupingUsesNumberRange(PivotFieldGrouping grouping) =>
        grouping == PivotFieldGrouping.NumberRange;

    /// <summary>The text box value for an optional grouping bound (blank when null).</summary>
    public static string FormatBound(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    public static PivotGroupFieldSubmission CaptureSubmission(
        IEnumerable<string> fieldNames,
        PivotFieldModel? currentField)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        var fields = fieldNames.ToList();
        var sourceFieldIndex = Math.Max(0, currentField?.SourceFieldIndex ?? 0);
        var sourceFieldName = sourceFieldIndex < fields.Count
            ? fields[sourceFieldIndex]
            : fields.Count > 0 ? fields[0] : string.Empty;
        var field = CreateField(
            sourceFieldIndex,
            currentField?.Grouping ?? PivotFieldGrouping.None,
            ungroup: false,
            currentField?.GroupStart,
            currentField?.GroupEnd,
            currentField?.GroupInterval);
        return new PivotGroupFieldSubmission(sourceFieldName.Trim(), field, Ungroup: false);
    }

    public static PivotGroupFieldSubmission CreateSubmission(
        string sourceFieldName,
        int sourceFieldIndex,
        PivotFieldGrouping grouping,
        bool ungroup,
        double? start,
        double? end,
        double? interval) =>
        new(
            (sourceFieldName ?? string.Empty).Trim(),
            CreateField(sourceFieldIndex, grouping, ungroup, start, end, interval),
            ungroup);

    public static bool TryCreateSubmission(
        string sourceFieldName,
        int sourceFieldIndex,
        PivotFieldGrouping grouping,
        bool ungroup,
        string? startText,
        string? endText,
        string? intervalText,
        out PivotGroupFieldSubmission? submission,
        out string? error)
    {
        if (!TryValidate(
                grouping,
                ungroup,
                startText,
                endText,
                intervalText,
                out var start,
                out var end,
                out var interval,
                out error))
        {
            submission = null;
            return false;
        }

        submission = CreateSubmission(
            sourceFieldName,
            sourceFieldIndex,
            grouping,
            ungroup,
            start,
            end,
            interval);
        return true;
    }

    /// <summary>
    /// Finds the existing layout field for <paramref name="sourceFieldIndex"/> (row, then column, then page),
    /// so the dialog can seed itself with the field's current grouping; null when the field is not yet placed.
    /// </summary>
    public static PivotFieldModel? FindLayoutField(PivotTableModel pivotTable, int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return FindInAxis(pivotTable.RowFields, sourceFieldIndex)
            ?? FindInAxis(pivotTable.ColumnFields, sourceFieldIndex)
            ?? FindInAxis(pivotTable.PageFields, sourceFieldIndex);
    }

    /// <summary>
    /// Validates the dialog's typed grouping inputs. Ungroup (or "do not group") needs no numeric inputs;
    /// otherwise the starting/ending bounds must be blank-or-numeric and a number-range grouping needs a
    /// positive "by" interval. On success yields the parsed bounds/interval.
    /// </summary>
    public static bool TryValidate(
        PivotFieldGrouping grouping,
        bool ungroup,
        string? startText,
        string? endText,
        string? intervalText,
        out double? start,
        out double? end,
        out double? interval,
        out string? error)
    {
        start = null;
        end = null;
        interval = null;
        error = null;

        if (ungroup || grouping == PivotFieldGrouping.None)
            return true;

        if (!TryParseOptionalFinite(startText, out start))
        {
            error = InvalidStartMessage;
            return false;
        }

        if (!TryParseOptionalFinite(endText, out end))
        {
            error = InvalidEndMessage;
            return false;
        }

        if (grouping == PivotFieldGrouping.NumberRange)
        {
            if (!TryParsePositive(intervalText, out var parsedInterval))
            {
                start = null;
                end = null;
                error = InvalidIntervalMessage;
                return false;
            }

            interval = parsedInterval;
        }

        return true;
    }

    /// <summary>
    /// Builds the grouped (or ungrouped) field for <paramref name="sourceFieldIndex"/>. Ungroup / "do not
    /// group" clears the grouping back to <see cref="PivotFieldGrouping.None"/> with no bounds; a date
    /// grouping carries the bounds only; a number-range grouping carries bounds plus a positive interval.
    /// </summary>
    public static PivotFieldModel CreateField(
        int sourceFieldIndex,
        PivotFieldGrouping grouping,
        bool ungroup,
        double? start,
        double? end,
        double? interval)
    {
        var normalizedIndex = Math.Max(0, sourceFieldIndex);
        if (ungroup || grouping == PivotFieldGrouping.None)
            return new PivotFieldModel(normalizedIndex, Grouping: PivotFieldGrouping.None);

        var normalizedInterval = grouping == PivotFieldGrouping.NumberRange
            ? Math.Max(1, interval ?? 1)
            : interval;

        return new PivotFieldModel(
            normalizedIndex,
            Grouping: grouping,
            GroupStart: start,
            GroupEnd: end,
            GroupInterval: normalizedInterval);
    }

    /// <summary>
    /// Rewrites the row/column/page layout lists so <paramref name="groupedField"/> replaces the matching
    /// field in whichever area holds it; if no area holds it the field is appended to the row area (so a
    /// fresh grouping still takes effect). Mirrors the desktop host's <c>ApplyPivotGroupingResult</c>.
    /// </summary>
    public static PivotGroupFieldLayout BuildLayout(PivotTableModel pivotTable, PivotFieldModel groupedField)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(groupedField);

        var alreadyPlaced = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .Any(field => field.SourceFieldIndex == groupedField.SourceFieldIndex);

        var rowFields = alreadyPlaced
            ? Replace(pivotTable.RowFields, groupedField)
            : pivotTable.RowFields.Append(groupedField).ToList();
        var columnFields = Replace(pivotTable.ColumnFields, groupedField);
        var pageFields = Replace(pivotTable.PageFields, groupedField);

        return new PivotGroupFieldLayout(rowFields, columnFields, pageFields);
    }

    private static List<PivotFieldModel> Replace(
        IReadOnlyList<PivotFieldModel> fields,
        PivotFieldModel replacement) =>
        fields
            .Select(field => field.SourceFieldIndex == replacement.SourceFieldIndex ? replacement : field)
            .ToList();

    private static PivotFieldModel? FindInAxis(IReadOnlyList<PivotFieldModel> fields, int sourceFieldIndex)
    {
        foreach (var field in fields)
        {
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        }

        return null;
    }

    private static bool TryParseOptionalFinite(string? text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        if ((!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
             !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) ||
            !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParsePositive(string? text, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return false;
        }

        var trimmed = text.Trim();
        if ((!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
             !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) ||
            !double.IsFinite(value) ||
            value <= 0)
        {
            value = 0;
            return false;
        }

        return true;
    }
}
