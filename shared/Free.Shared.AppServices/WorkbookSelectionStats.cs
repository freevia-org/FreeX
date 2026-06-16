namespace Free.Shared.AppServices;

public readonly record struct WorkbookSelectionStats(
    double Sum,
    int Count,
    int NumericalCount,
    double? Average,
    double? Min,
    double? Max)
{
    public bool IsEmpty => Count == 0;

    public bool HasNumericalValues => NumericalCount > 0;
}
