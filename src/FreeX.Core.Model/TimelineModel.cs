namespace FreeX.Core.Model;

public sealed class TimelineModel
{
    public string Name { get; init; } = "";
    public string? Caption { get; init; }
    public string CacheName { get; init; } = "";
    public string? SourcePivotTableName { get; set; }

    /// <summary>
    /// Every pivot table name this timeline's cache lists as a connection (Excel's
    /// <c>timelineCacheDefinition/pivotTables/pivotTable/@name</c>), in document order. See
    /// <see cref="SlicerModel.ConnectedPivotTableNames"/> for the full rationale -- identical shape, shared
    /// by the same rewrite function on the Core.IO save path.
    /// </summary>
    public List<string> ConnectedPivotTableNames { get; init; } = [];

    public string? SourceFieldName { get; init; }
    public string? StyleName { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? SelectedStartDate { get; set; }
    public string? SelectedEndDate { get; set; }
    public string PackagePart { get; init; } = "";
    public DrawingAnchorRange? DrawingAnchor { get; init; }
    public string? DrawingShapeName { get; init; }
    public string? SourceSheetName { get; set; }
    /// <summary>
    /// The OOXML <c>level</c> attribute from the timeline definition part (xl/timelines/*.xml).
    /// Maps to the Excel date hierarchy: 0=years, 1=quarters, 2=months, 3=days.
    /// Null when the attribute is absent (older files without explicit granularity).
    /// </summary>
    public int? Level { get; set; }
    /// <summary>
    /// The OOXML <c>selectionLevel</c> attribute from the timeline definition part.
    /// Controls which date hierarchy level drives the current selection (independent of
    /// <see cref="Level"/>, which controls the display granularity). Null when absent —
    /// in that case the writer falls back to emitting <see cref="Level"/> for this attribute,
    /// preserving the pre-fix behaviour for files where the two values coincide.
    /// </summary>
    public int? SelectionLevel { get; init; }
    /// <summary>
    /// The OOXML <c>scrollPosition</c> attribute from the timeline definition part — the date/time
    /// of the first visible period in the timeline's scroll window. Null when absent.
    /// </summary>
    public string? ScrollPosition { get; init; }
}
