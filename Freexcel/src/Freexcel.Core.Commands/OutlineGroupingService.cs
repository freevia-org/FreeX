using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum OutlineGroupingAxis
{
    Rows,
    Columns
}

public static class OutlineGroupingService
{
    public static OutlineGroupingAxis GetGroupingAxis(GridRange range) =>
        SelectionRangeService.IsWholeColumnSelection(range)
            ? OutlineGroupingAxis.Columns
            : OutlineGroupingAxis.Rows;
}
