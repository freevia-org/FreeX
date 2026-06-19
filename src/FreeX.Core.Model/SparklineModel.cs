namespace FreeX.Core.Model;

public enum SparklineKind
{
    Line,
    Column,
    WinLoss
}

public static class SparklineRangeLimits
{
    public const long MaxDataCellCount = 4096;

    public static bool IsSupportedDataRange(GridRange range) =>
        range.CellCount <= MaxDataCellCount;
}

public sealed class SparklineModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GridRange DataRange { get; set; }
    public CellAddress Location { get; set; }
    public SparklineKind Kind { get; set; } = SparklineKind.Line;

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

    /// <summary>Optional series color; when null the renderer uses its default sparkline color.</summary>
    public CellColor? SeriesColor { get; set; }
}
