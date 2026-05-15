namespace Freexcel.Core.Model;

public enum SparklineKind
{
    Line,
    Column,
    WinLoss
}

public sealed class SparklineModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GridRange DataRange { get; set; }
    public CellAddress Location { get; set; }
    public SparklineKind Kind { get; set; } = SparklineKind.Line;
}
