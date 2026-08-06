using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// A request to move a source field into a target bucket at a given position. <see cref="TargetIndex"/>
/// is the zero-based insertion position within the target bucket; a value at or past the bucket's current
/// length (or a negative value) appends. Moving to <see cref="PivotFieldBucket.Available"/> removes the
/// field from every layout area. Avalonia supplies <see cref="SourceBucket"/> and <see cref="SourceItemIndex"/>
/// for an exact Values-area drag; older callers may omit them and retain source-field matching behavior.
/// </summary>
public sealed record PivotFieldDropRequest(
    int SourceFieldIndex,
    PivotFieldBucket TargetBucket,
    int TargetIndex = -1,
    PivotFieldBucket? SourceBucket = null,
    int SourceItemIndex = -1);

/// <summary>
/// The outcome of validating a <see cref="PivotFieldDropRequest"/>. When <see cref="IsAllowed"/> is true,
/// <see cref="ResultingLayout"/> describes the four layout areas after the move (as ordered source-field
/// index lists) and, for a drop into the values area, <see cref="DefaultSummaryFunction"/> is the
/// aggregation a new data field should use. When false, <see cref="RejectionReason"/> explains why.
/// </summary>
public sealed record PivotFieldDropResult(
    bool IsAllowed,
    PivotFieldDropRequest Request,
    PivotLayoutPlan? ResultingLayout = null,
    string? DefaultSummaryFunction = null,
    string? RejectionReason = null)
{
    public static PivotFieldDropResult Rejected(PivotFieldDropRequest request, string reason) =>
        new(false, request, RejectionReason: reason);
}

/// <summary>
/// The ordered source-field index membership of the four layout areas. The values area can list the same
/// source field more than once (a field may be aggregated several ways), which is why it is a plain list.
/// </summary>
public sealed record PivotLayoutPlan(
    IReadOnlyList<int> Rows,
    IReadOnlyList<int> Columns,
    IReadOnlyList<int> Filters,
    IReadOnlyList<int> Values);

/// <summary>
/// Validates field drag/drop requests against a <see cref="PivotTableModel"/> and produces the resulting
/// layout without mutating anything. Honors each field's per-area drag permissions
/// (<c>DragToRow</c>/<c>DragToColumn</c>/<c>DragToPage</c>/<c>DragToData</c>) when present. Ported from the
/// field-list drop logic in the desktop hosts; renderers turn an allowed result into a pivot mutation.
/// </summary>
public sealed class PivotFieldDragValidator
{
    private readonly Func<int, bool> _isNumericSourceField;

    /// <summary>
    /// Creates a validator. <paramref name="isNumericSourceField"/> reports whether a source field column
    /// holds numeric data, which drives the values-area aggregation default (sum for numeric, otherwise
    /// count). When omitted, every field is treated as non-numeric.
    /// </summary>
    public PivotFieldDragValidator(Func<int, bool>? isNumericSourceField = null) =>
        _isNumericSourceField = isNumericSourceField ?? (_ => false);

    public PivotFieldDropResult Validate(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldDropRequest request)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return Validate(PivotFieldLayoutPlanner.Capture(pivotTable), headers, request);
    }

    public PivotFieldDropResult Validate(
        PivotFieldAreas areas,
        IReadOnlyList<string> headers,
        PivotFieldDropRequest request)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceFieldIndex < 0 || request.SourceFieldIndex >= headers.Count)
            return PivotFieldDropResult.Rejected(request, "Source field is out of range.");

        if (!IsDragAllowed(areas, request.SourceFieldIndex, request.TargetBucket))
            return PivotFieldDropResult.Rejected(request, $"Field cannot be placed in {request.TargetBucket}.");

        var layout = ApplyMove(areas, request);
        var defaultSummary = request.TargetBucket == PivotFieldBucket.Values
            ? DefaultSummaryFunction(request.SourceFieldIndex)
            : null;

        return new PivotFieldDropResult(true, request, layout, defaultSummary);
    }

    /// <summary>
    /// The aggregation a freshly-dropped values-area field should use: sum for a numeric source column,
    /// count otherwise. Ported from the default-data-field logic in the desktop hosts.
    /// </summary>
    public string DefaultSummaryFunction(int sourceFieldIndex) =>
        _isNumericSourceField(sourceFieldIndex)
            ? PivotAggregationFunctions.Sum.FunctionCode
            : PivotAggregationFunctions.Count.FunctionCode;

    private bool IsDragAllowed(PivotFieldAreas areas, int sourceFieldIndex, PivotFieldBucket bucket)
    {
        if (bucket == PivotFieldBucket.Available)
            return true;

        var field = FindLayoutField(areas, sourceFieldIndex);
        if (field is null)
            return true;

        return bucket switch
        {
            PivotFieldBucket.Rows => field.DragToRow ?? true,
            PivotFieldBucket.Columns => field.DragToColumn ?? true,
            PivotFieldBucket.Filters => field.DragToPage ?? true,
            PivotFieldBucket.Values => field.DragToData ?? true,
            _ => true
        };
    }

    private static PivotFieldModel? FindLayoutField(PivotFieldAreas areas, int sourceFieldIndex) =>
        FindIn(areas.RowFields, sourceFieldIndex)
        ?? FindIn(areas.ColumnFields, sourceFieldIndex)
        ?? FindIn(areas.PageFields, sourceFieldIndex);

    private static PivotFieldModel? FindIn(IReadOnlyList<PivotFieldModel> fields, int sourceFieldIndex)
    {
        foreach (var field in fields)
        {
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        }

        return null;
    }

    private static PivotLayoutPlan ApplyMove(PivotFieldAreas areas, PivotFieldDropRequest request)
    {
        var rows = areas.RowFields.Select(field => field.SourceFieldIndex).ToList();
        var columns = areas.ColumnFields.Select(field => field.SourceFieldIndex).ToList();
        var filters = areas.PageFields.Select(field => field.SourceFieldIndex).ToList();
        var values = areas.DataFields.Select(field => field.SourceFieldIndex).ToList();

        var index = request.SourceFieldIndex;

        // Removing the field from the row/column/filter areas keeps a layout field in at most one of them.
        rows.Remove(index);
        columns.Remove(index);
        filters.Remove(index);

        if (request.SourceBucket == PivotFieldBucket.Values)
            RemoveValueField(values, index, request.SourceItemIndex);

        switch (request.TargetBucket)
        {
            case PivotFieldBucket.Rows:
                Insert(rows, index, request.TargetIndex);
                break;
            case PivotFieldBucket.Columns:
                Insert(columns, index, request.TargetIndex);
                break;
            case PivotFieldBucket.Filters:
                Insert(filters, index, request.TargetIndex);
                break;
            case PivotFieldBucket.Values:
                Insert(values, index, request.TargetIndex);
                break;
            case PivotFieldBucket.Available:
                if (request.SourceBucket != PivotFieldBucket.Values)
                    values.RemoveAll(value => value == index);
                break;
        }

        return new PivotLayoutPlan(rows, columns, filters, values);
    }

    private static void Insert(List<int> items, int item, int index)
    {
        if (index < 0 || index > items.Count)
            items.Add(item);
        else
            items.Insert(index, item);
    }

    private static void RemoveValueField(List<int> values, int sourceFieldIndex, int sourceItemIndex)
    {
        if ((uint)sourceItemIndex < (uint)values.Count && values[sourceItemIndex] == sourceFieldIndex)
        {
            values.RemoveAt(sourceItemIndex);
            return;
        }

        var matchingIndex = values.IndexOf(sourceFieldIndex);
        if (matchingIndex >= 0)
            values.RemoveAt(matchingIndex);
    }
}
