using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    // COUNTIF(S) criteria can match cells outside the used range. Keep the full
    // reference geometry and examine only positions with an effective nonblank value.
    private bool TryEvaluateSparseFullAxisCount(
        FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        int pairCount = node.Arguments.Count / 2;
        var ranges = new SparseCountRange[pairCount];
        bool hasFullAxis = false;
        for (int i = 0; i < pairCount; i++)
        {
            if (!TryGetSparseCountRange(node.Arguments[i * 2], context, out ranges[i]))
                return false;
            var range = ranges[i].Range;
            hasFullAxis |= (range.StartRow == 1 && range.EndRow == CellAddress.MaxRow) ||
                           (range.StartCol == 1 && range.EndCol == CellAddress.MaxCol);
        }
        if (!hasFullAxis)
            return false;

        var shape = ranges[0].Range;
        var criteria = new BuiltInFunctions.CriteriaMatcher[pairCount];
        bool blankMatches = true;
        for (int i = 0; i < pairCount; i++)
        {
            if (!SameDirectRangeShape(shape, ranges[i].Range))
            {
                result = ErrorValue.Value;
                return true;
            }
            var state = TryCreateConditionalCriteria(node.Arguments[i * 2 + 1], context, out criteria[i], out result);
            if (state == DirectRangeFastPathState.Unsupported)
                return false;
            if (state == DirectRangeFastPathState.Error)
                return true;
            blankMatches &= criteria[i].Matches(BlankValue.Instance);
        }

        // Match a hypothetical all-blank grid first, then correct only positions
        // where at least one criteria range contains a value. Relative offsets align
        // differently located ranges and work even when they are on different sheets.
        long total = (long)DirectRangeRowCount(shape) * DirectRangeColCount(shape);
        long count = blankMatches ? total : 0;
        // If a criterion rejects blanks, its nonblank positions alone are a
        // complete candidate set. Otherwise visit each distinct range, skipping
        // positions already covered by an earlier range without storing offsets.
        int anchor = -1;
        long anchorCost = long.MaxValue;
        for (int i = 0; i < pairCount; i++)
        {
            if (criteria[i].Matches(BlankValue.Instance)) continue;
            long cost = SparseRangeScanCost(ranges[i]);
            if (cost < anchorCost) { anchor = i; anchorCost = cost; }
        }
        var unique = new List<SparseCountRange>();
        if (anchor >= 0) unique.Add(ranges[anchor]);
        else foreach (var range in ranges)
        {
            if (!unique.Any(previous => SameSparseRange(previous, range)))
                unique.Add(range);
        }
        for (int source = 0; source < unique.Count; source++)
        {
            var item = unique[source];
            foreach (var address in EnumerateSparseRangeValues(item))
            {
                uint row = address.Row - item.Range.StartRow;
                uint col = address.Col - item.Range.StartCol;
                bool visited = false;
                for (int previous = 0; previous < source; previous++)
                {
                    var prior = unique[previous];
                    if (prior.Sheet.GetValue(prior.Range.StartRow + row, prior.Range.StartCol + col) is not BlankValue)
                    {
                        visited = true;
                        break;
                    }
                }
                if (visited) continue;
                bool matches = true;
                for (int i = 0; i < pairCount; i++)
                {
                    var range = ranges[i].Range;
                    if (!criteria[i].Matches(ranges[i].Sheet.GetValue(range.StartRow + row, range.StartCol + col)))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches != blankMatches)
                    count += matches ? 1 : -1;
            }
        }
        result = NumberValueFor(count);
        return true;
    }

    // A SUMIF(S)/AVERAGEIF(S) value must itself be numeric (or an error), so
    // only value-bearing cells in the sum range can affect the result. Criteria
    // are evaluated at the corresponding nominal offset, including blank cells.
    private bool TryEvaluateSparseFullAxisSumAverage(
        FunctionCallNode node, IEvalContext context, bool average, bool multipleCriteria, out ScalarValue result)
    {
        result = BlankValue.Instance;
        int count = multipleCriteria ? (node.Arguments.Count - 1) / 2 : 1;
        int firstCriteria = multipleCriteria ? 1 : 0;
        var ranges = new SparseCountRange[count];
        bool fullAxis = false;
        for (int i = 0; i < count; i++)
        {
            if (!TryGetSparseCountRange(node.Arguments[firstCriteria + i * 2], context, out ranges[i]))
                return false;
            var r = ranges[i].Range;
            fullAxis |= (r.StartRow == 1 && r.EndRow == CellAddress.MaxRow) ||
                        (r.StartCol == 1 && r.EndCol == CellAddress.MaxCol);
        }
        SparseCountRange values;
        if (multipleCriteria || node.Arguments.Count == 3)
        {
            var valueIndex = multipleCriteria ? 0 : 2;
            if (!TryGetSparseCountRange(node.Arguments[valueIndex], context, out values))
                return false;
        }
        else values = ranges[0];

        var valueShape = values.Range;
        fullAxis |= (valueShape.StartRow == 1 && valueShape.EndRow == CellAddress.MaxRow) ||
                    (valueShape.StartCol == 1 && valueShape.EndCol == CellAddress.MaxCol);
        var shape = multipleCriteria ? values.Range : ranges[0].Range;
        fullAxis |= (shape.StartRow == 1 && shape.EndRow == CellAddress.MaxRow) ||
                    (shape.StartCol == 1 && shape.EndCol == CellAddress.MaxCol);
        if (!fullAxis) return false;

        // SUMIF's optional sum range is projected from its top-left to the
        // criteria range's size; the multi-criteria variants require equal shape.
        if (!multipleCriteria)
        {
            ulong lastRow = (ulong)values.Range.StartRow + DirectRangeRowCount(shape) - 1;
            ulong lastCol = (ulong)values.Range.StartCol + DirectRangeColCount(shape) - 1;
            if (lastRow > CellAddress.MaxRow || lastCol > CellAddress.MaxCol)
                return false;
            values = values with { Range = values.Range with { EndRow = (uint)lastRow, EndCol = (uint)lastCol } };
        }
        var criteria = new BuiltInFunctions.CriteriaMatcher[count];
        for (int i = 0; i < count; i++)
        {
            if (!SameDirectRangeShape(shape, ranges[i].Range))
            {
                result = ErrorValue.Value;
                return true;
            }
            var state = TryCreateConditionalCriteria(node.Arguments[firstCriteria + i * 2 + 1], context, out criteria[i], out result);
            if (state == DirectRangeFastPathState.Unsupported) return false;
            if (state == DirectRangeFastPathState.Error) return true;
        }
        bool MatchesCriteria(CellAddress address)
        {
            var vr = values.Range;
            uint row = address.Row - vr.StartRow, col = address.Col - vr.StartCol;
            for (int i = 0; i < count; i++)
            {
                var r = ranges[i].Range;
                if (!criteria[i].Matches(ranges[i].Sheet.GetValue(r.StartRow + row, r.StartCol + col)))
                    return false;
            }
            return true;
        }
        // Find the first matching error without buffering numeric values. Sparse
        // enumeration is unordered, so retain the lowest row-major address.
        ErrorValue? firstError = null;
        uint firstErrorRow = uint.MaxValue, firstErrorCol = uint.MaxValue;
        foreach (var address in EnumerateSparseRangeValues(values))
        {
            if (values.Sheet.GetValue(address.Row, address.Col) is ErrorValue error &&
                (address.Row < firstErrorRow || (address.Row == firstErrorRow && address.Col < firstErrorCol)) &&
                MatchesCriteria(address))
            {
                firstError = error;
                firstErrorRow = address.Row;
                firstErrorCol = address.Col;
            }
        }
        if (firstError is not null)
        {
            result = firstError;
            return true;
        }

        // Discard unmatched and nonnumeric values before ordering, preserving
        // row-major floating-point accumulation without buffering rejected cells.
        bool IsRelevantNumber(CellAddress address) =>
            TryDirectRangeNumber(values.Sheet.GetValue(address.Row, address.Col), out _, out _) &&
            MatchesCriteria(address);
        double sum = 0;
        long numericCount = 0;
        foreach (var address in EnumerateSparseRangeValues(values, IsRelevantNumber, ordered: true))
        {
            TryDirectRangeNumber(values.Sheet.GetValue(address.Row, address.Col), out var number, out _);
            sum += number;
            numericCount++;
        }
        result = average && numericCount == 0 ? ErrorValue.DivByZero :
            FastNumberResult(average ? sum / numericCount : sum);
        return true;
    }

    private static bool SameSparseRange(SparseCountRange left, SparseCountRange right) =>
        left.Sheet == right.Sheet && left.Range.StartRow == right.Range.StartRow &&
        left.Range.StartCol == right.Range.StartCol && left.Range.EndRow == right.Range.EndRow &&
        left.Range.EndCol == right.Range.EndCol;

    private static long SparseRangeScanCost(SparseCountRange item) =>
        Math.Min((long)DirectRangeRowCount(item.Range) * DirectRangeColCount(item.Range),
            (long)item.Sheet.CellCount + item.Sheet.SpillValueCount);

    private static IEnumerable<CellAddress> EnumerateSparseRangeValues(
        SparseCountRange item, Func<CellAddress, bool>? predicate = null, bool ordered = false)
    {
        var range = item.Range;
        long cells = (long)DirectRangeRowCount(range) * DirectRangeColCount(range);
        if (cells <= (long)item.Sheet.CellCount + item.Sheet.SpillValueCount &&
            cells <= FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            for (uint row = range.StartRow; row <= range.EndRow; row++)
                for (uint col = range.StartCol; col <= range.EndCol; col++)
                    if (item.Sheet.GetValue(row, col) is not BlankValue)
                    {
                        var address = new CellAddress(item.Sheet.Id, row, col);
                        if (predicate is null || predicate(address)) yield return address;
                    }
            yield break;
        }
        var addresses = item.Sheet.EnumerateValueBearingCells().Where(address =>
            address.Row >= range.StartRow && address.Row <= range.EndRow &&
            address.Col >= range.StartCol && address.Col <= range.EndCol);
        if (predicate is not null) addresses = addresses.Where(predicate);
        if (ordered) addresses = addresses.OrderBy(address => address.Row).ThenBy(address => address.Col);
        foreach (var address in addresses) yield return address;
    }

    private static bool TryGetSparseCountRange(FormulaNode node, IEvalContext context, out SparseCountRange result)
    {
        result = default;
        Sheet? sheet;
        uint startRow, startCol, endRow, endCol;
        string? sheetName;
        if (node is NamedRangeNode named)
        {
            if (context.TryResolveLambdaBinding(named.Name) is not null ||
                (IsSheetScopedName(named.Name, context, out var isFormula) && isFormula))
                return false;
            var namedRange = context.TryResolveNamedRange(named.Name);
            if (namedRange is not { } grid || grid.Start.Sheet != grid.End.Sheet)
                return false;
            sheet = context.CurrentSheet?.Id == grid.Start.Sheet
                ? context.CurrentSheet : context.CurrentWorkbook?.GetSheet(grid.Start.Sheet);
            if (sheet is null) return false;
            sheetName = sheet.Name;
            startRow = grid.Start.Row; startCol = grid.Start.Col;
            endRow = grid.End.Row; endCol = grid.End.Col;
        }
        else
        {
            RangeRefNode reference;
            if (node is CellRefNode cell)
                reference = new RangeRefNode(cell, cell, cell.SheetName);
            else if (!TryAsRangeRef(node, out reference))
                return false;
            sheetName = reference.SheetName;
            sheet = sheetName is null ? context.CurrentSheet : context.CurrentWorkbook?.GetSheet(sheetName);
            if (sheet is null && sheetName is not null && context.CurrentSheet is { } current &&
                string.Equals(current.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                sheet = current;
            // External references do not expose a complete local sparse cell set.
            if (sheet is null) return false;
            startRow = reference.Start.Row; startCol = reference.Start.ColumnNumber;
            endRow = reference.End.Row; endCol = reference.End.ColumnNumber;
        }
        result = new SparseCountRange(sheet, new DirectRangeArgument(sheetName,
            Math.Min(startRow, endRow), Math.Min(startCol, endCol),
            Math.Max(startRow, endRow), Math.Max(startCol, endCol)));
        return true;
    }

    private readonly record struct SparseCountRange(Sheet Sheet, DirectRangeArgument Range);
}
