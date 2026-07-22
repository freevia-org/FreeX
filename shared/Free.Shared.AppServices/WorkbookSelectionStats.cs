namespace Free.Shared.AppServices;

public readonly record struct WorkbookSelectionStats(
    double Sum,
    int Count,
    int NumericalCount,
    double? Average,
    double? Min,
    double? Max,
    string? AggregateErrorCode = null)
{
    public bool IsEmpty => Count == 0;

    public bool HasNumericalValues => NumericalCount > 0;

    /// <summary>
    /// True when the selection contains at least one error cell (e.g. #DIV/0!) among the cells
    /// that would otherwise contribute to Sum/Average/Min/Max. Matches Excel: the status bar's
    /// aggregate readouts propagate the error instead of silently excluding the erroring cell(s)
    /// from the computation, while Count/NumericalCount keep counting normally.
    /// </summary>
    public bool HasAggregateError => AggregateErrorCode is not null;
}
