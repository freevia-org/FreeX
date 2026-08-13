using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// The four sort modes offered by the "More Sort Options" dialog for a pivot field.
/// </summary>
public enum PivotSortOptionMode
{
    LabelAscending,
    LabelDescending,
    ValueAscending,
    ValueDescending,
}

public sealed record PivotSortOptionDescriptor(
    PivotSortOptionMode Mode,
    ResourceTextDescriptor Text,
    string AutomationId);

/// <summary>
/// Portable, UI-free planning for the PivotTable "More Sort Options" dialog. Resolves the dialog's initial
/// mode/value-field selection from the field's current <see cref="PivotSortModel"/>, validates a value-sort
/// (a value field must exist), and builds the resulting <see cref="PivotSortModel"/>. Single-sourced here so
/// every desktop host shares identical behavior. Field-sort replacement is shared here and the Pivot
/// application session owns the resulting view command.
/// </summary>
public static class PivotSortPlanner
{
    public static IReadOnlyList<PivotSortOptionDescriptor> Options { get; } =
    [
        Option(PivotSortOptionMode.LabelAscending, "PivotSort_AscendingByLabels", "Ascending (A to Z) by labels", "PivotSortOptionsLabelAscending"),
        Option(PivotSortOptionMode.LabelDescending, "PivotSort_DescendingByLabels", "Descending (Z to A) by labels", "PivotSortOptionsLabelDescending"),
        Option(PivotSortOptionMode.ValueAscending, "PivotSort_AscendingByValues", "Ascending by values", "PivotSortOptionsValueAscending"),
        Option(PivotSortOptionMode.ValueDescending, "PivotSort_DescendingByValues", "Descending by values", "PivotSortOptionsValueDescending"),
    ];

    public static ResourceTextDescriptor ValueSortRequiresValueField { get; } = new(
        "PivotSort_ValueFieldRequired",
        "Add a PivotTable value field before sorting by values.");

    public static PivotSortOptionDescriptor GetOption(PivotSortOptionMode mode) =>
        Options.First(option => option.Mode == mode);

    /// <summary>The initial dialog mode for a field, from its current sort (defaults to label-ascending).</summary>
    public static PivotSortOptionMode InitialMode(PivotSortModel? currentSort, int sourceFieldIndex)
    {
        if (currentSort is null || currentSort.FieldIndex != sourceFieldIndex)
            return PivotSortOptionMode.LabelAscending;

        if (currentSort.Target == PivotSortTarget.Value)
            return currentSort.Direction == PivotSortDirection.Descending
                ? PivotSortOptionMode.ValueDescending
                : PivotSortOptionMode.ValueAscending;

        return currentSort.Direction == PivotSortDirection.Descending
            ? PivotSortOptionMode.LabelDescending
            : PivotSortOptionMode.LabelAscending;
    }

    /// <summary>The initial value-field combo selection for the field, clamped to the data-field range.</summary>
    public static int InitialValueFieldIndex(
        PivotSortModel? currentSort,
        int sourceFieldIndex,
        int dataFieldCount)
    {
        if (dataFieldCount <= 0)
            return -1;

        if (currentSort is { Target: PivotSortTarget.Value } sort &&
            sort.FieldIndex == sourceFieldIndex &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < dataFieldCount)
        {
            return sort.DataFieldIndex;
        }

        return 0;
    }

    public static bool IsValueSort(PivotSortOptionMode mode) =>
        mode is PivotSortOptionMode.ValueAscending or PivotSortOptionMode.ValueDescending;

    /// <summary>True when the value-field selector should be enabled for the chosen mode.</summary>
    public static bool ValueFieldEnabled(PivotSortOptionMode mode, int dataFieldCount) =>
        IsValueSort(mode) && dataFieldCount > 0;

    /// <summary>Validates that a value sort has a selectable data field.</summary>
    public static bool TryValidate(
        PivotSortOptionMode mode,
        int dataFieldCount,
        int valueFieldSelectedIndex,
        out ResourceTextDescriptor? error)
    {
        error = null;
        if (!IsValueSort(mode))
            return true;

        if (dataFieldCount <= 0 || valueFieldSelectedIndex < 0 || valueFieldSelectedIndex >= dataFieldCount)
        {
            error = ValueSortRequiresValueField;
            return false;
        }

        return true;
    }

    /// <summary>Builds the resulting sort from the dialog's collected input.</summary>
    public static PivotSortModel CreateResult(
        PivotSortOptionMode mode,
        int sourceFieldIndex,
        int valueFieldSelectedIndex)
    {
        if (IsValueSort(mode))
        {
            return new PivotSortModel(
                PivotSortTarget.Value,
                mode == PivotSortOptionMode.ValueDescending
                    ? PivotSortDirection.Descending
                    : PivotSortDirection.Ascending,
                DataFieldIndex: Math.Max(0, valueFieldSelectedIndex),
                FieldIndex: sourceFieldIndex);
        }

        return new PivotSortModel(
            PivotSortTarget.Label,
            mode == PivotSortOptionMode.LabelDescending
                ? PivotSortDirection.Descending
                : PivotSortDirection.Ascending,
            FieldIndex: sourceFieldIndex);
    }

    /// <summary>
    /// Replaces the field's existing sort (label sorts match the field index; value sorts match the same
    /// field index) with <paramref name="sort"/>, returning the full updated sort list to hand to the view
    /// command. Mirrors the per-field replacement the header sort actions use.
    /// </summary>
    public static IReadOnlyList<PivotSortModel> ReplaceFieldSort(
        IReadOnlyList<PivotSortModel> existingSorts,
        PivotSortModel sort)
    {
        ArgumentNullException.ThrowIfNull(existingSorts);
        ArgumentNullException.ThrowIfNull(sort);

        return existingSorts
            .Where(existing => existing.FieldIndex != sort.FieldIndex)
            .Append(sort)
            .ToList();
    }

    public static IReadOnlyList<PivotSortModel> ReplaceQuickSort(
        IReadOnlyList<PivotSortModel> existingSorts,
        int? sourceFieldIndex,
        int? dataFieldIndex,
        int axisFieldIndex,
        PivotSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(existingSorts);
        if (sourceFieldIndex is null && dataFieldIndex is null)
            return existingSorts.ToList();

        var replacement = dataFieldIndex is { } valueFieldIndex
            ? new PivotSortModel(
                PivotSortTarget.Value,
                direction,
                DataFieldIndex: valueFieldIndex,
                FieldIndex: axisFieldIndex)
            : new PivotSortModel(
                PivotSortTarget.Label,
                direction,
                FieldIndex: sourceFieldIndex!.Value);

        return existingSorts
            .Where(existing =>
                (sourceFieldIndex is null || existing.FieldIndex != sourceFieldIndex.Value) &&
                (dataFieldIndex is null || existing.DataFieldIndex != dataFieldIndex.Value))
            .Append(replacement)
            .ToList();
    }

    private static PivotSortOptionDescriptor Option(
        PivotSortOptionMode mode,
        string resourceKey,
        string fallbackLabel,
        string automationId) =>
        new(mode, new ResourceTextDescriptor(resourceKey, fallbackLabel), automationId);
}
