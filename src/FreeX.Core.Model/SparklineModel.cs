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
}
