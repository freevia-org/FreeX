using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotAvailableFieldItemModel(
    int SourceFieldIndex,
    string Caption,
    bool IsChecked);

/// <summary>
/// A single field as shown in the field-list pane. For a field placed in <see cref="PivotFieldBucket.Values"/>
/// the <see cref="Caption"/> is the data field's display name (e.g. "Sum of Amount") and
/// <see cref="DataFieldIndex"/> identifies which entry in the pivot's data fields it maps to; for fields in
/// the other areas the caption is the source column header and <see cref="DataFieldIndex"/> is null.
/// </summary>
public sealed record PivotFieldListItemModel(
    int SourceFieldIndex,
    string Caption,
    PivotFieldBucket Bucket,
    int? DataFieldIndex = null,
    string? SummaryFunction = null);

/// <summary>
/// A field-list bucket: the ordered fields currently inside one of the layout areas. The
/// <see cref="PivotFieldBucket.Available"/> bucket lists every source field not yet placed in a
/// layout area (checking one moves it into a layout area; unchecking removes it).
/// </summary>
public sealed record PivotFieldListBucketModel(
    PivotFieldBucket Bucket,
    IReadOnlyList<PivotFieldListItemModel> Fields)
{
    public bool IsEmpty => Fields.Count == 0;
}

/// <summary>
/// Portable description of the pivot field-list pane: the pool of available source fields plus the
/// four layout-area buckets and the fields placed in them. Derived from a <see cref="PivotTableModel"/>
/// and the source-range headers; carries no rendering concerns.
/// </summary>
public sealed record PivotFieldListPaneModel(
    string PivotTableName,
    PivotFieldListBucketModel Available,
    PivotFieldListBucketModel Rows,
    PivotFieldListBucketModel Columns,
    PivotFieldListBucketModel Values,
    PivotFieldListBucketModel Filters)
{
    public PivotFieldListBucketModel Bucket(PivotFieldBucket bucket) =>
        bucket switch
        {
            PivotFieldBucket.Available => Available,
            PivotFieldBucket.Rows => Rows,
            PivotFieldBucket.Columns => Columns,
            PivotFieldBucket.Values => Values,
            PivotFieldBucket.Filters => Filters,
            _ => throw new ArgumentOutOfRangeException(nameof(bucket))
        };

    /// <summary>All buckets in display order (available pool first, then the four layout areas).</summary>
    public IReadOnlyList<PivotFieldListBucketModel> AllBuckets =>
        [Available, Rows, Columns, Values, Filters];
}
