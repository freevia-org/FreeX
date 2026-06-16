namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// The four layout areas a pivot field can occupy. <see cref="Available"/> represents the
/// pool of source fields that are not yet placed in any layout area, which the field-list pane
/// shows above the four area buckets.
/// </summary>
public enum PivotFieldBucket
{
    /// <summary>Source fields not currently placed in any layout area.</summary>
    Available,

    /// <summary>Row labels (<c>RowFields</c>).</summary>
    Rows,

    /// <summary>Column labels (<c>ColumnFields</c>).</summary>
    Columns,

    /// <summary>The values area (<c>DataFields</c>), where aggregations live.</summary>
    Values,

    /// <summary>Report/page filters (<c>PageFields</c>).</summary>
    Filters
}
