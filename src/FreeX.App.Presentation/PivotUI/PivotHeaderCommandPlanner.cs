using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>Which kind of pivot command a header action maps to (so callers/tests can assert intent).</summary>
public enum PivotHeaderCommandKind
{
    None,
    Layout,
    View,
}

/// <summary>
/// The outcome of mapping a header-dropdown menu action to a workbook command. A
/// <see cref="PivotHeaderCommandPlan"/> is either <see cref="PivotHeaderCommandPlan.Command"/>-bearing (the shell executes it
/// and refreshes), <see cref="IsNoOp"/> (the action is valid but produces no change — e.g. clearing a sort
/// that is not set), or <see cref="IsDeferred"/> with a <see cref="DeferredReason"/> (the action needs a
/// dialog or a command that does not exist yet, so the shell skips it). For executable results the
/// <see cref="Kind"/> plus the planned <see cref="Areas"/>/<see cref="Sorts"/>/filter lists expose the exact
/// command parameters so the mapping can be asserted without applying the command against a workbook.
/// </summary>
public sealed record PivotHeaderCommandPlan(
    IWorkbookCommand? Command,
    PivotHeaderCommandKind Kind = PivotHeaderCommandKind.None,
    bool IsNoOp = false,
    bool IsDeferred = false,
    string? DeferredReason = null,
    PivotFieldAreas? Areas = null,
    IReadOnlyList<PivotSortModel>? Sorts = null,
    IReadOnlyList<PivotLabelFilterModel>? LabelFilters = null,
    IReadOnlyList<PivotValueFilterModel>? ValueFilters = null)
{
    public static PivotHeaderCommandPlan NoOp { get; } = new(null, IsNoOp: true);

    public static PivotHeaderCommandPlan Deferred(string reason) =>
        new(null, IsDeferred: true, DeferredReason: reason);
}

/// <summary>
/// Renderer-neutral mapping from a chosen <see cref="PivotHeaderMenuAction"/> (from
/// <see cref="PivotHeaderDropdownMenuBuilder.BuildMenu"/>) to the workbook command the shell executes. Sort
/// and clear actions build a <see cref="ConfigurePivotTableViewCommand"/>; move/remove actions reuse the
/// shared <see cref="PivotFieldDragValidator"/> + <see cref="PivotFieldLayoutPlanner"/> so a header
/// "Move to Columns"/"Remove Field" follows the exact same layout rules as a field-pane drag. Actions that
/// require a dialog (filters, field settings, custom sort) remain deferred at this UI-free boundary because
/// the shell routes them through existing dialog continuations.
/// This is shared application policy; renderers retain menu realization and dialog lifecycle only.
/// </summary>
public static class PivotHeaderCommandPlanner
{
    public static PivotHeaderCommandPlan Create(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action,
        PivotFieldDragValidator validator)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(validator);

        return action switch
        {
            PivotHeaderMenuAction.Separator => PivotHeaderCommandPlan.NoOp,
            PivotHeaderMenuAction.SortAscending =>
                Sort(sheetId, pivotTable, target, PivotSortDirection.Ascending),
            PivotHeaderMenuAction.SortDescending =>
                Sort(sheetId, pivotTable, target, PivotSortDirection.Descending),
            PivotHeaderMenuAction.ClearSort => ClearSort(sheetId, pivotTable, target),
            PivotHeaderMenuAction.ClearFilter => ClearFilter(sheetId, pivotTable, target),
            PivotHeaderMenuAction.MoveToRows =>
                Move(sheetId, pivotTable, headers, target, PivotFieldBucket.Rows, validator),
            PivotHeaderMenuAction.MoveToColumns =>
                Move(sheetId, pivotTable, headers, target, PivotFieldBucket.Columns, validator),
            PivotHeaderMenuAction.MoveToFilters =>
                Move(sheetId, pivotTable, headers, target, PivotFieldBucket.Filters, validator),
            PivotHeaderMenuAction.MoveToValues =>
                Move(sheetId, pivotTable, headers, target, PivotFieldBucket.Values, validator),
            PivotHeaderMenuAction.MoveUp => Reorder(sheetId, pivotTable, headers, target, -1, validator),
            PivotHeaderMenuAction.MoveDown => Reorder(sheetId, pivotTable, headers, target, +1, validator),
            PivotHeaderMenuAction.RemoveField =>
                Move(sheetId, pivotTable, headers, target, PivotFieldBucket.Available, validator),
            PivotHeaderMenuAction.LabelFilter =>
                PivotHeaderCommandPlan.Deferred("Label filter is routed through the existing dialog continuation."),
            PivotHeaderMenuAction.ValueFilter =>
                PivotHeaderCommandPlan.Deferred("Value filter is routed through the existing dialog continuation."),
            PivotHeaderMenuAction.MoreSortOptions =>
                PivotHeaderCommandPlan.Deferred("More Sort Options is routed through the existing dialog continuation."),
            PivotHeaderMenuAction.FieldSettings =>
                PivotHeaderCommandPlan.Deferred("Field Settings is routed through the existing dialog continuation."),
            PivotHeaderMenuAction.ValueFieldSettings =>
                PivotHeaderCommandPlan.Deferred("Value Field Settings is routed through the existing dialog continuation."),
            _ => PivotHeaderCommandPlan.Deferred($"Unhandled pivot header action {action}."),
        };
    }

    // A value-area header sorts that data field; a label/page/row/column header sorts the field's labels.
    private static PivotHeaderCommandPlan Sort(
        SheetId sheetId,
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target,
        PivotSortDirection direction)
    {
        PivotSortModel sort;
        Func<PivotSortModel, bool> isSameField;
        if (target.Area == PivotHeaderArea.Value)
        {
            var dataFieldIndex = target.DataFieldIndex ?? FindDataFieldIndex(pivotTable, target.SourceFieldIndex);
            if (dataFieldIndex is null)
                return PivotHeaderCommandPlan.NoOp;

            sort = new PivotSortModel(PivotSortTarget.Value, direction, DataFieldIndex: dataFieldIndex.Value);
            isSameField = existing =>
                existing.Target == PivotSortTarget.Value && existing.DataFieldIndex == dataFieldIndex.Value;
        }
        else
        {
            sort = new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: target.SourceFieldIndex);
            isSameField = existing =>
                existing.Target == PivotSortTarget.Label && existing.FieldIndex == target.SourceFieldIndex;
        }

        var sorts = pivotTable.Sorts.Where(existing => !isSameField(existing)).Append(sort).ToList();
        return ViewCommand(sheetId, pivotTable, sorts: sorts);
    }

    // Shared layout-command result builder, capturing the resolved field areas for assertion.
    private static PivotHeaderCommandPlan LayoutResult(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldDropResult dropResult)
    {
        if (!dropResult.IsAllowed || dropResult.ResultingLayout is not { } layout || layout.Values.Count == 0)
            return PivotHeaderCommandPlan.NoOp;

        var areas = PivotFieldLayoutPlanner.BuildAreas(
            pivotTable, headers, layout, dropResult.DefaultSummaryFunction);
        var command = new ConfigurePivotTableLayoutCommand(
            sheetId,
            pivotTable.Name,
            areas.RowFields,
            areas.ColumnFields,
            areas.PageFields,
            areas.DataFields);
        return new PivotHeaderCommandPlan(command, PivotHeaderCommandKind.Layout, Areas: areas);
    }

    private static PivotHeaderCommandPlan ClearSort(
        SheetId sheetId,
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target)
    {
        var dataFieldIndex = target.Area == PivotHeaderArea.Value
            ? target.DataFieldIndex ?? FindDataFieldIndex(pivotTable, target.SourceFieldIndex)
            : null;

        var sorts = pivotTable.Sorts
            .Where(sort => !SortMatchesTarget(sort, target, dataFieldIndex))
            .ToList();
        if (sorts.Count == pivotTable.Sorts.Count)
            return PivotHeaderCommandPlan.NoOp;

        return ViewCommand(sheetId, pivotTable, sorts: sorts);
    }

    private static bool SortMatchesTarget(
        PivotSortModel sort,
        PivotHeaderDropdownTargetModel target,
        int? dataFieldIndex) =>
        target.Area == PivotHeaderArea.Value
            ? sort.Target == PivotSortTarget.Value && dataFieldIndex is { } index && sort.DataFieldIndex == index
            : sort.FieldIndex == target.SourceFieldIndex;

    // Clear both the label and value filters that belong to the field (the header badge counts either).
    private static PivotHeaderCommandPlan ClearFilter(
        SheetId sheetId,
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target)
    {
        var labelFilters = pivotTable.LabelFilters
            .Where(filter => filter.SourceFieldIndex != target.SourceFieldIndex)
            .ToList();
        var valueFilters = pivotTable.ValueFilters
            .Where(filter => !PivotFilterOwnership.BelongsToSourceField(filter, target.SourceFieldIndex))
            .ToList();

        if (labelFilters.Count == pivotTable.LabelFilters.Count &&
            valueFilters.Count == pivotTable.ValueFilters.Count)
        {
            return PivotHeaderCommandPlan.NoOp;
        }

        return ViewCommand(sheetId, pivotTable, labelFilters: labelFilters, valueFilters: valueFilters);
    }

    // Move/remove route through the same validate → layout-command path the field pane uses.
    private static PivotHeaderCommandPlan Move(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotFieldBucket bucket,
        PivotFieldDragValidator validator)
    {
        var request = new PivotFieldDropRequest(target.SourceFieldIndex, bucket);
        var result = validator.Validate(pivotTable, headers, request);
        return LayoutResult(sheetId, pivotTable, headers, result);
    }

    // Reorder within the field's current area by re-inserting at an adjacent index.
    private static PivotHeaderCommandPlan Reorder(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        int delta,
        PivotFieldDragValidator validator)
    {
        var (bucket, fields) = ResolveAxis(pivotTable, target.Area);
        if (fields is null)
            return PivotHeaderCommandPlan.NoOp;

        var currentIndex = IndexOfSourceField(fields, target.SourceFieldIndex);
        if (currentIndex < 0)
            return PivotHeaderCommandPlan.NoOp;

        var targetIndex = currentIndex + delta;
        if (targetIndex < 0 || targetIndex >= fields.Count)
            return PivotHeaderCommandPlan.NoOp;

        var request = new PivotFieldDropRequest(target.SourceFieldIndex, bucket, targetIndex);
        var result = validator.Validate(pivotTable, headers, request);
        return LayoutResult(sheetId, pivotTable, headers, result);
    }

    private static (PivotFieldBucket Bucket, IReadOnlyList<PivotFieldModel>? Fields) ResolveAxis(
        PivotTableModel pivotTable,
        PivotHeaderArea area) =>
        area switch
        {
            PivotHeaderArea.Row => (PivotFieldBucket.Rows, pivotTable.RowFields),
            PivotHeaderArea.Column => (PivotFieldBucket.Columns, pivotTable.ColumnFields),
            PivotHeaderArea.Page => (PivotFieldBucket.Filters, pivotTable.PageFields),
            _ => (PivotFieldBucket.Values, null),
        };

    private static int IndexOfSourceField(IReadOnlyList<PivotFieldModel> fields, int sourceFieldIndex)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].SourceFieldIndex == sourceFieldIndex)
                return index;
        }

        return -1;
    }

    private static int? FindDataFieldIndex(PivotTableModel pivotTable, int sourceFieldIndex)
    {
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            if (pivotTable.DataFields[index].SourceFieldIndex == sourceFieldIndex)
                return index;
        }

        return null;
    }

    private static PivotHeaderCommandPlan ViewCommand(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel>? labelFilters = null,
        IReadOnlyList<PivotValueFilterModel>? valueFilters = null,
        IReadOnlyList<PivotSortModel>? sorts = null)
    {
        var resolvedLabels = labelFilters ?? pivotTable.LabelFilters.ToList();
        var resolvedValues = valueFilters ?? pivotTable.ValueFilters.ToList();
        var resolvedSorts = sorts ?? pivotTable.Sorts.ToList();
        var command = new ConfigurePivotTableViewCommand(
            sheetId, pivotTable.Name, resolvedLabels, resolvedValues, resolvedSorts);
        return new PivotHeaderCommandPlan(
            command,
            PivotHeaderCommandKind.View,
            Sorts: resolvedSorts,
            LabelFilters: resolvedLabels,
            ValueFilters: resolvedValues);
    }
}
