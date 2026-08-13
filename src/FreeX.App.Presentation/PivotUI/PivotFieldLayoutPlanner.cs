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
    public static PivotFieldAreas Capture(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return new PivotFieldAreas(
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.DataFields.ToList());
    }

    public static PivotFieldLayoutDropPlan PlanDrop(
        PivotFieldAreas current,
        IReadOnlyList<string> headers,
        PivotFieldDropRequest request,
        PivotFieldDragValidator validator)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(current, headers, request);
        if (!result.IsAllowed || result.ResultingLayout is null)
            return new PivotFieldLayoutDropPlan(result, null);

        var areas = ApplyConcreteDrop(current, headers, request, result.DefaultSummaryFunction);
        return new PivotFieldLayoutDropPlan(result, areas.DataFields.Count == 0 ? null : areas);
    }

    public static int? ResolveSourceFieldIndex(
        PivotFieldAreas areas,
        IReadOnlyList<string> headers,
        string? caption,
        PivotFieldBucket? sourceBucket = null,
        int sourceItemIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(headers);

        var indexed = sourceBucket switch
        {
            PivotFieldBucket.Rows => SourceIndexAt(areas.RowFields, sourceItemIndex),
            PivotFieldBucket.Columns => SourceIndexAt(areas.ColumnFields, sourceItemIndex),
            PivotFieldBucket.Filters => SourceIndexAt(areas.PageFields, sourceItemIndex),
            PivotFieldBucket.Values => SourceIndexAt(areas.DataFields, sourceItemIndex),
            _ => null,
        };
        if (indexed is not null)
            return indexed;

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
        if (sourceIndex is not null)
            return sourceIndex;

        if (string.IsNullOrWhiteSpace(caption))
            return null;

        return areas.DataFields
            .FirstOrDefault(field => string.Equals(
                field.Name,
                caption,
                StringComparison.CurrentCultureIgnoreCase))
            ?.SourceFieldIndex;
    }

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
        return BuildAreas(Capture(pivotTable), headers, layout, defaultSummaryFunction);
    }

    public static PivotFieldAreas BuildAreas(
        PivotFieldAreas current,
        IReadOnlyList<string> headers,
        PivotLayoutPlan layout,
        string? defaultSummaryFunction)
    {
        var existingFields = new Dictionary<int, PivotFieldModel>();
        StashFields(current.RowFields, existingFields);
        StashFields(current.ColumnFields, existingFields);
        StashFields(current.PageFields, existingFields);

        var rows = BuildAxis(layout.Rows, existingFields);
        var columns = BuildAxis(layout.Columns, existingFields);
        var pages = BuildAxis(layout.Filters, existingFields);
        var data = BuildData(layout.Values, current.DataFields, headers, defaultSummaryFunction);

        return new PivotFieldAreas(rows, columns, pages, data);
    }

    private static PivotFieldAreas ApplyConcreteDrop(
        PivotFieldAreas current,
        IReadOnlyList<string> headers,
        PivotFieldDropRequest request,
        string? defaultSummaryFunction)
    {
        var rows = current.RowFields.ToList();
        var columns = current.ColumnFields.ToList();
        var pages = current.PageFields.ToList();
        var data = current.DataFields.ToList();
        var sourceIndex = request.SourceFieldIndex;
        var axisField = FindAxisField(current, sourceIndex) ?? new PivotFieldModel(sourceIndex);
        PivotDataFieldModel? dataField = null;

        rows.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
        columns.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
        pages.RemoveAll(field => field.SourceFieldIndex == sourceIndex);

        if (request.SourceBucket == PivotFieldBucket.Values)
        {
            dataField = RemoveDataField(data, sourceIndex, request.SourceItemIndex);
        }
        else if (request.TargetBucket == PivotFieldBucket.Available)
        {
            data.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
        }

        switch (request.TargetBucket)
        {
            case PivotFieldBucket.Rows:
                Insert(rows, axisField, request.TargetIndex);
                break;
            case PivotFieldBucket.Columns:
                Insert(columns, axisField, request.TargetIndex);
                break;
            case PivotFieldBucket.Filters:
                Insert(pages, axisField, request.TargetIndex);
                break;
            case PivotFieldBucket.Values:
                dataField ??= CreateDataField(headers, sourceIndex, defaultSummaryFunction);
                Insert(data, dataField, request.TargetIndex);
                break;
        }

        return new PivotFieldAreas(rows, columns, pages, data);
    }

    private static PivotFieldModel? FindAxisField(PivotFieldAreas areas, int sourceIndex) =>
        areas.RowFields
            .Concat(areas.ColumnFields)
            .Concat(areas.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceIndex);

    private static PivotDataFieldModel? RemoveDataField(
        List<PivotDataFieldModel> fields,
        int sourceIndex,
        int sourceItemIndex)
    {
        if ((uint)sourceItemIndex < (uint)fields.Count &&
            fields[sourceItemIndex].SourceFieldIndex == sourceIndex)
        {
            var exact = fields[sourceItemIndex];
            fields.RemoveAt(sourceItemIndex);
            return exact;
        }

        var index = fields.FindIndex(field => field.SourceFieldIndex == sourceIndex);
        if (index < 0)
            return null;

        var first = fields[index];
        fields.RemoveAt(index);
        return first;
    }

    private static PivotDataFieldModel CreateDataField(
        IReadOnlyList<string> headers,
        int sourceIndex,
        string? defaultSummaryFunction)
    {
        var summary = string.IsNullOrWhiteSpace(defaultSummaryFunction)
            ? PivotAggregationFunctions.Count.FunctionCode
            : defaultSummaryFunction;
        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, sourceIndex);
        return new PivotDataFieldModel(sourceIndex, DefaultDataFieldName(summary, caption), summary);
    }

    private static void Insert<T>(List<T> fields, T field, int index)
    {
        if (index < 0 || index > fields.Count)
            fields.Add(field);
        else
            fields.Insert(index, field);
    }

    private static int? SourceIndexAt(IReadOnlyList<PivotFieldModel> fields, int index) =>
        (uint)index < (uint)fields.Count ? fields[index].SourceFieldIndex : null;

    private static int? SourceIndexAt(IReadOnlyList<PivotDataFieldModel> fields, int index) =>
        (uint)index < (uint)fields.Count ? fields[index].SourceFieldIndex : null;

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

public sealed record PivotFieldLayoutDropPlan(
    PivotFieldDropResult Result,
    PivotFieldAreas? Areas)
{
    public bool CanApply => Result.IsAllowed && Areas is not null;
}

public sealed record PivotFieldLayoutDraft(
    string PivotTableName,
    PivotFieldAreas Areas)
{
    public IReadOnlyList<PivotFieldModel> RowFields => Areas.RowFields;
    public IReadOnlyList<PivotFieldModel> ColumnFields => Areas.ColumnFields;
    public IReadOnlyList<PivotFieldModel> PageFields => Areas.PageFields;
    public IReadOnlyList<PivotDataFieldModel> DataFields => Areas.DataFields;
}
