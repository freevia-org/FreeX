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

    /// <summary>
    /// True once <see cref="SelectedItems"/> has been explicitly set by a user action (a
    /// <c>SetSlicerSelectionCommand</c> apply), as opposed to still holding its post-load default. An
    /// empty <see cref="SelectedItems"/> is ambiguous on its own: it means either "the model never
    /// touched the selection" (native <c>s</c> flags on a loaded workbook must be preserved verbatim) or
    /// "the user explicitly cleared the filter to select-all" (native <c>s</c> flags must be stripped so
    /// the clear round-trips). This flag disambiguates the two for the Core.IO slicer/timeline state
    /// rewriter that runs on save of a source-preserved workbook.
    /// </summary>
    public bool SelectionCaptured { get; set; }

    public string PackagePart { get; init; } = "";
    public DrawingAnchorRange? DrawingAnchor { get; init; }
    public string? DrawingShapeName { get; init; }

    /// <summary>Number of tile columns the slicer lays its item buttons out in (Excel's <c>columnCount</c>). Defaults to 1.</summary>
    public int ColumnCount { get; init; } = 1;

    /// <summary>
    /// Whether the slicer draws its caption header band (Excel's <c>showCaption</c>; default true). When
    /// false the renderer omits the blue caption band and lays the item tiles out from the top of the box,
    /// matching Excel.
    /// </summary>
    public bool ShowCaption { get; init; } = true;

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
    /// <para>
    /// R117-commands-pivot-slicer-growth: <c>List</c> (not a read-only projection) so
    /// <see cref="PivotTableRefreshService"/> can APPEND an entry for an index that newly exists in the
    /// bound <see cref="PivotCacheFieldModel.SharedItems"/> after a refresh -- SharedItems is
    /// append-only (R115/R116), so a value that appears for the first time gets a brand-new index at
    /// the END of that list that no previously-built CacheItems entry represents. Still assignable via
    /// an object initializer (<c>init</c>) at construction/load time exactly like before; the added
    /// mutability only enables appending post-construction, it never changes how a whole-list
    /// assignment behaves. Any code that copies a <see cref="SlicerModel"/> (e.g.
    /// <c>DuplicateSheetDrawingCloner</c>) must copy this list (<c>.ToList()</c>), not alias it, or the
    /// clone and the original would share -- and silently co-mutate -- the same backing list.
    /// </para>
    /// </summary>
    public List<SlicerCacheItem> CacheItems { get; init; } = [];

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
