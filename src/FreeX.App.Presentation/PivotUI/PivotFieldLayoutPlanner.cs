using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Renderer-neutral planning that turns an <em>allowed</em> <see cref="PivotFieldDropResult"/> (produced by
/// <see cref="PivotFieldDragValidator"/>) into the <see cref="ConfigurePivotTableLayoutCommand"/> the shell
/// executes to apply a field-pane drag. The validator yields only the source-field membership of the four
/// layout areas (a <see cref="PivotLayoutPlan"/>); this factory rebuilds the concrete
/// <see cref="PivotFieldModel"/>/<see cref="PivotDataFieldModel"/> lists, preserving every existing field's
/// per-field state (drag permissions, selections, aggregation/format) and synthesizing a fresh data field
/// with the validator's default aggregation when a brand-new field lands in the values area.
/// Shared by WPF and Avalonia so field preservation, aggregation defaults, and command composition are
/// application policy rather than renderer glue.
/// </summary>
public static class PivotFieldLayoutPlanner
{
    /// <summary>
    /// Builds the layout command for an allowed drop. Returns null when the result is not allowed, carries no
    /// resulting layout, or the move would leave the values area empty (the layout command rejects a pivot
    /// with no data field, so the shell should ignore the drag instead of surfacing a guard failure).
    /// </summary>
    public static ConfigurePivotTableLayoutCommand? TryCreateCommand(
        SheetId sheetId,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldDropResult result)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsAllowed || result.ResultingLayout is not { } layout)
            return null;
        if (layout.Values.Count == 0)
            return null;

        var areas = BuildAreas(pivotTable, headers, layout, result.DefaultSummaryFunction);
        return new ConfigurePivotTableLayoutCommand(
            sheetId,
            pivotTable.Name,
            areas.RowFields,
            areas.ColumnFields,
            areas.PageFields,
            areas.DataFields);
    }

    /// <summary>
    /// Rebuilds the four concrete field lists from a resolved <see cref="PivotLayoutPlan"/>. Exposed
    /// (internal) so the mapping is unit-testable independently of command construction.
    /// </summary>
    public static PivotFieldAreas BuildAreas(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotLayoutPlan layout,
        string? defaultSummaryFunction)
    {
        var existingFields = new Dictionary<int, PivotFieldModel>();
        StashFields(pivotTable.RowFields, existingFields);
        StashFields(pivotTable.ColumnFields, existingFields);
        StashFields(pivotTable.PageFields, existingFields);

        var rows = BuildAxis(layout.Rows, existingFields);
        var columns = BuildAxis(layout.Columns, existingFields);
        var pages = BuildAxis(layout.Filters, existingFields);
        var data = BuildData(layout.Values, pivotTable.DataFields, headers, defaultSummaryFunction);

        return new PivotFieldAreas(rows, columns, pages, data);
    }

    private static void StashFields(
        IReadOnlyList<PivotFieldModel> fields,
        Dictionary<int, PivotFieldModel> sink)
    {
        foreach (var field in fields)
            sink.TryAdd(field.SourceFieldIndex, field);
    }

    // Reuse the existing field model (drag flags, selections, grouping) when the field was already placed in
    // an axis; otherwise materialize a default field for the source column.
    private static List<PivotFieldModel> BuildAxis(
        IReadOnlyList<int> sourceIndices,
        IReadOnlyDictionary<int, PivotFieldModel> existingFields)
    {
        var result = new List<PivotFieldModel>(sourceIndices.Count);
        foreach (var sourceIndex in sourceIndices)
        {
            result.Add(existingFields.TryGetValue(sourceIndex, out var existing)
                ? existing
                : new PivotFieldModel(sourceIndex));
        }

        return result;
    }

    // The values plan lists source indices (possibly repeated). Consume existing data fields positionally by
    // source index so their aggregation/format/name survive a reorder; any surplus index becomes a new data
    // field using the validator's default aggregation.
    private static List<PivotDataFieldModel> BuildData(
        IReadOnlyList<int> sourceIndices,
        IReadOnlyList<PivotDataFieldModel> existingData,
        IReadOnlyList<string> headers,
        string? defaultSummaryFunction)
    {
        var pools = new Dictionary<int, Queue<PivotDataFieldModel>>();
        foreach (var dataField in existingData)
        {
            if (!pools.TryGetValue(dataField.SourceFieldIndex, out var queue))
            {
                queue = new Queue<PivotDataFieldModel>();
                pools[dataField.SourceFieldIndex] = queue;
            }

            queue.Enqueue(dataField);
        }

        var result = new List<PivotDataFieldModel>(sourceIndices.Count);
        foreach (var sourceIndex in sourceIndices)
        {
            if (pools.TryGetValue(sourceIndex, out var queue) && queue.Count > 0)
            {
                result.Add(queue.Dequeue());
                continue;
            }

            var summary = string.IsNullOrWhiteSpace(defaultSummaryFunction)
                ? PivotAggregationFunctions.Count.FunctionCode
                : defaultSummaryFunction!;
            var caption = PivotFieldListPaneBuilder.FieldCaption(headers, sourceIndex);
            result.Add(new PivotDataFieldModel(
                sourceIndex,
                DefaultDataFieldName(summary, caption),
                summary));
        }

        return result;
    }

    private static string DefaultDataFieldName(string summaryFunction, string caption)
    {
        var function = PivotAggregationFunctions.FromCode(summaryFunction) ?? PivotAggregationFunctions.Count;
        return $"{function.DisplayName} of {caption}";
    }
}

/// <summary>The four concrete pivot field lists a layout command consumes.</summary>
public sealed record PivotFieldAreas(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields,
    IReadOnlyList<PivotDataFieldModel> DataFields);
