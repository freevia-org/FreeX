namespace FreeX.Core.Model;

public sealed class SlicerModel
{
    public string Name { get; init; } = "";
    public string? Caption { get; init; }
    public string CacheName { get; init; } = "";
    public string? SourcePivotTableName { get; set; }
    public string? SourceFieldName { get; init; }
    public string? StyleName { get; init; }
    public List<string> SelectedItems { get; } = [];
    public string PackagePart { get; init; } = "";
    public DrawingAnchorRange? DrawingAnchor { get; init; }
    public string? DrawingShapeName { get; init; }

    /// <summary>Number of tile columns the slicer lays its item buttons out in (Excel's <c>columnCount</c>). Defaults to 1.</summary>
    public int ColumnCount { get; init; } = 1;

    /// <summary>
    /// Name of the worksheet whose drawing hosts this slicer's anchor. Table slicers have no
    /// <see cref="SourcePivotTableName"/>, so the visibility gate uses this to decide whether the slicer
    /// belongs on the active sheet. Resolved from the worksheet → drawing relationship chain on load.
    /// </summary>
    public string? SourceSheetName { get; set; }

    /// <summary>Structured-table id this slicer filters (table slicers only; <c>tableSlicerCache/@tableId</c>). Null for pivot slicers.</summary>
    public int? SourceTableId { get; init; }

    /// <summary>Structured-table column id this slicer filters (table slicers only; <c>tableSlicerCache/@column</c>). Null for pivot slicers.</summary>
    public int? SourceTableColumnId { get; init; }

    /// <summary>
    /// Cached available-item indices and selection flags parsed from a pivot slicer cache's
    /// <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;</c> (the <c>x</c> index + <c>s</c> selected flag). The string
    /// captions are resolved from the pivot cache field's shared items at viewport-build time.
    /// Empty for table slicers (which resolve items from the referenced table column instead).
    /// </summary>
    public IReadOnlyList<SlicerCacheItem> CacheItems { get; init; } = [];

    /// <summary>
    /// The resolved, ordered list of item captions offered by this slicer's source (table column distinct
    /// values, or pivot cache shared items). Populated just before render by the slicer item resolver,
    /// mirroring how form-control selected text is resolved at viewport-build time. Empty until resolved.
    /// </summary>
    public IReadOnlyList<string> AvailableItems { get; set; } = [];
}

/// <summary>One entry in a pivot slicer cache's tabular item list: the field-item index and whether it is selected.</summary>
public sealed record SlicerCacheItem(int Index, bool IsSelected);

public sealed record DrawingAnchorPoint(
    uint Column,
    long ColumnOffsetEmu,
    uint Row,
    long RowOffsetEmu);

public sealed record DrawingAnchorRange(
    DrawingAnchorPoint From,
    DrawingAnchorPoint To);
