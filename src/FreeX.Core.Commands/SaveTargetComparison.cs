using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r258: content comparison for the two records the "save this again" commands replace --
/// <see cref="WorkbookCustomView"/> and <see cref="WorkbookScenario"/>.
///
/// <para>r231 named these two precisely and declined to guard them: "both targets ARE records, and
/// the obvious guard is <c>newValue == previous</c> -- but both records carry LIST members, which
/// record equality compares by reference, so against a freshly built instance it is always false.
/// That guard would never fire while looking exactly like the ones that do work." This is the
/// comparison that does fire. Each record has exactly ONE collection member, so the strip-and-compare
/// shape used from r253 onward applies directly: replace the list with a shared instance, let record
/// equality cover the scalars (and keep covering a scalar added later), compare the list by content.
/// </para>
///
/// <para>The element comparisons differ in kind, and neither is <c>==</c> by accident:
/// <see cref="WorksheetCustomViewState"/> has its own thirty-member comparer from r248, with its own
/// coverage contract; <see cref="ScenarioCellValue"/> is a pair of a <c>CellAddress</c> and a
/// <c>ScalarValue</c>, both value-equality records with no collection member of their own, which
/// <c>R258_SaveTargetComparisonCoverageContractTests</c> checks rather than assumes.</para>
/// </summary>
internal static class SaveTargetComparison
{
    private static readonly IReadOnlyList<WorksheetCustomViewState> NoSheets = [];
    private static readonly IReadOnlyList<ScenarioCellValue> NoCells = [];

    internal static bool Same(WorkbookCustomView left, WorkbookCustomView right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if ((left with { Sheets = NoSheets }) != (right with { Sheets = NoSheets }))
            return false;

        if (left.Sheets.Count != right.Sheets.Count)
            return false;

        for (var i = 0; i < left.Sheets.Count; i++)
        {
            if (!WorksheetCustomViewStateComparer.Same(left.Sheets[i], right.Sheets[i]))
                return false;
        }

        return true;
    }

    internal static bool Same(WorkbookScenario left, WorkbookScenario right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if ((left with { ChangingCells = NoCells }) != (right with { ChangingCells = NoCells }))
            return false;

        if (left.ChangingCells.Count != right.ChangingCells.Count)
            return false;

        // Order is content: a scenario's changing cells round-trip in the order they are written,
        // and the values line up with the cell list positionally.
        for (var i = 0; i < left.ChangingCells.Count; i++)
        {
            if (!SameCellValue(left.ChangingCells[i], right.ChangingCells[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// <c>ScenarioCellValue</c> is a record of a <c>CellAddress</c> and a <c>ScalarValue</c>, and
    /// record equality would be content equality for it -- except for one subtype.
    /// <see cref="RangeValue"/> carries a <c>ScalarValue[,]</c>, and an array is compared by
    /// REFERENCE, so two ranges holding identical values compare unequal. That is the same trap as
    /// the list members one level up, and it was found by the coverage contract rather than by
    /// reading: <c>ScalarValue</c> is abstract, so the array is only reachable through a subtype the
    /// declared member type does not mention.
    /// </summary>
    private static bool SameCellValue(ScenarioCellValue left, ScenarioCellValue right)
    {
        if (left.Address != right.Address)
            return false;

        if (left.Value is RangeValue leftRange && right.Value is RangeValue rightRange)
            return SameRange(leftRange, rightRange);

        return left.Value == right.Value;
    }

    private static bool SameRange(RangeValue left, RangeValue right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if ((left with { Cells = EmptyCells }) != (right with { Cells = EmptyCells }))
            return false;

        var rows = left.Cells.GetLength(0);
        var cols = left.Cells.GetLength(1);
        if (rows != right.Cells.GetLength(0) || cols != right.Cells.GetLength(1))
            return false;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                // Recurses: a range can hold a range.
                if (left.Cells[row, col] is RangeValue nestedLeft && right.Cells[row, col] is RangeValue nestedRight)
                {
                    if (!SameRange(nestedLeft, nestedRight))
                        return false;
                }
                else if (left.Cells[row, col] != right.Cells[row, col])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static readonly ScalarValue[,] EmptyCells = new ScalarValue[0, 0];
}
