namespace FreeX.Core.Model;

public enum SparklineKind
{
    Line,
    Column,
    WinLoss
}

/// <summary>Controls how the sparkline group's value axis minimum/maximum is determined.</summary>
public enum SparklineAxisScaling
{
    /// <summary>Each sparkline computes its own min/max independently.</summary>
    Individual,
    /// <summary>All sparklines in the group share the same min/max (across the group).</summary>
    Group,
    /// <summary>A user-supplied fixed value is used as the axis bound.</summary>
    Custom,
}

/// <summary>Controls how empty (blank) cells are rendered in a sparkline.</summary>
public enum SparklineEmptyCellDisplay
{
    /// <summary>Leave a gap in the sparkline line/bar at blank cells.</summary>
    Gap,
    /// <summary>Treat blank cells as zero.</summary>
    Zero,
    /// <summary>Connect across blank cells (line sparklines only).</summary>
    Span,
}

public static class SparklineRangeLimits
{
    public const long MaxDataCellCount = 4096;

    public static bool IsSupportedDataRange(GridRange range) =>
        range.CellCount <= MaxDataCellCount;
}

/// <summary>
/// Allocates the nonzero <see cref="SparklineModel.GroupId"/> values shared by every member of a
/// multi-sparkline group created in-app (Excel's "Insert Sparklines" dialog with a multi-row/column
/// Location Range, or the Quick Analysis Sparklines gesture). <see cref="SparklineModel.GroupId"/> == 0
/// means "ungrouped" (each such sparkline becomes its own singleton group on save), so a freshly
/// inserted group must be assigned an id that is both nonzero and not already in use on the sheet.
/// </summary>
public static class SparklineGroupIdAllocator
{
    /// <summary>
    /// Returns a group id guaranteed not to collide with any <see cref="SparklineModel.GroupId"/>
    /// already present on <paramref name="existingSparklines"/>.
    /// </summary>
    public static int NextGroupId(IEnumerable<SparklineModel> existingSparklines)
    {
        var maxExisting = 0;
        foreach (var sparkline in existingSparklines)
        {
            if (sparkline.GroupId > maxExisting)
                maxExisting = sparkline.GroupId;
        }

        return maxExisting + 1;
    }
}

public sealed class SparklineModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GridRange DataRange { get; set; }
    public CellAddress Location { get; set; }
    public SparklineKind Kind { get; set; } = SparklineKind.Line;

    // ── Group identity (assigned at XLSX read to preserve per-group settings) ──
    /// <summary>
    /// Identifies the sparkline group this sparkline belongs to.
    /// On XLSX read each &lt;x14:sparklineGroup&gt; element gets a unique integer key;
    /// sparklines from the same group share the same key so they can be round-tripped
    /// as a single group even when multiple same-type groups exist.
    /// </summary>
    public int GroupId { get; set; }

    // ── Show-flags ─────────────────────────────────────────────────────────────

    /// <summary>Draw a marker at every data point (line sparklines).</summary>
    public bool ShowMarkers { get; set; }

    /// <summary>Emphasize the highest data point.</summary>
    public bool ShowHighPoint { get; set; }

    /// <summary>Emphasize the lowest data point.</summary>
    public bool ShowLowPoint { get; set; }

    /// <summary>Emphasize the first data point.</summary>
    public bool ShowFirstPoint { get; set; }

    /// <summary>Emphasize the last data point.</summary>
    public bool ShowLastPoint { get; set; }

    /// <summary>Emphasize negative data points (column / win-loss sparklines).</summary>
    public bool ShowNegativePoints { get; set; }

    /// <summary>Show a horizontal axis line when the data crosses zero.</summary>
    public bool ShowAxis { get; set; }

    /// <summary>Include hidden cells in the sparkline data range.</summary>
    public bool DisplayHidden { get; set; }

    /// <summary>Plot the sparkline right-to-left.</summary>
    public bool RightToLeft { get; set; }

    // ── Colors ─────────────────────────────────────────────────────────────────

    /// <summary>Optional series color; when null the renderer uses its default sparkline color.</summary>
    public CellColor? SeriesColor { get; set; }

    /// <summary>Color used for negative data points.</summary>
    public CellColor? NegativeColor { get; set; }

    /// <summary>Color used for the horizontal axis.</summary>
    public CellColor? AxisColor { get; set; }

    /// <summary>Color used for markers (line sparklines).</summary>
    public CellColor? MarkersColor { get; set; }

    /// <summary>Color used for the highest data point.</summary>
    public CellColor? HighPointColor { get; set; }

    /// <summary>Color used for the lowest data point.</summary>
    public CellColor? LowPointColor { get; set; }

    /// <summary>Color used for the first data point.</summary>
    public CellColor? FirstPointColor { get; set; }

    /// <summary>Color used for the last data point.</summary>
    public CellColor? LastPointColor { get; set; }

    // ── Appearance ─────────────────────────────────────────────────────────────

    /// <summary>Line weight in points for line sparklines; null = default (0.75 pt).</summary>
    public double? LineWeight { get; set; }

    // ── Axis scaling ───────────────────────────────────────────────────────────

    /// <summary>How the minimum value axis bound is determined. Default is Individual.</summary>
    public SparklineAxisScaling MinAxisType { get; set; } = SparklineAxisScaling.Individual;

    /// <summary>How the maximum value axis bound is determined. Default is Individual.</summary>
    public SparklineAxisScaling MaxAxisType { get; set; } = SparklineAxisScaling.Individual;

    /// <summary>Fixed minimum value when <see cref="MinAxisType"/> is Custom.</summary>
    public double? ManualMin { get; set; }

    /// <summary>Fixed maximum value when <see cref="MaxAxisType"/> is Custom.</summary>
    public double? ManualMax { get; set; }

    // ── Empty-cell handling ────────────────────────────────────────────────────

    /// <summary>How blank/empty cells are displayed. Default is Gap.</summary>
    public SparklineEmptyCellDisplay DisplayEmptyCellsAs { get; set; } = SparklineEmptyCellDisplay.Gap;

    // ── Date axis ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The optional date axis range (Excel's "Date Axis Type" sparkline group setting), spacing data
    /// points along a time axis instead of evenly. A group-level setting like <see cref="MinAxisType"/>
    /// and the color properties: every member of a group shares the same value, and
    /// <c>XlsxSparklineMapper</c> reads/writes it once per &lt;x14:sparklineGroup&gt; from the
    /// group's representative sparkline. Null when the group has no date axis (the common case).
    /// </summary>
    public GridRange? DateAxisRange { get; set; }
}
