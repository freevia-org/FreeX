namespace FreeX.Core.Commands;

public sealed record SubtotalInputOptions(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceExisting,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData);
