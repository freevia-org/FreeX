using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Conditional aggregation functions.

    // ── Array-criteria helpers ──────────────────────────────────────────────────
    //
    // Excel rule: when a *IF(S) function receives a range/array where a scalar
    // criteria value is expected, it evaluates element-by-element and returns an
    // array of results with the same shape.  SUMPRODUCT (and array formulas) then
    // consume that array.
    //
    // We detect the array-criteria case at the top of each function and dispatch
    // to a loop that substitutes each element in turn, collecting results into a
    // RangeValue of matching shape.  Scalar-criteria paths are byte-identical to
    // the previous behaviour.
    //
    // Only ONE criteria argument may be a range at a time (per Excel semantics).
    // If multiple criteria args are RangeValues, only the first one encountered
    // triggers the array expansion; the rest are treated as parallel-criteria
    // ranges (normal behaviour) or produce #VALUE! if their shape differs.

    /// <summary>
    /// Returns (pairIndex, criteriaRangeArg) for the first criteria argument
    /// (among the *IF(S) criteria slots) that is a RangeValue, or null if all
    /// criteria are scalars.
    /// <para>
    /// For SUMIFS/COUNTIFS/AVERAGEIFS <paramref name="firstCriteriaArgIndex"/>
    /// is the index of the first criteria-value slot (not the criteria-range
    /// slot).  For SUMIF/COUNTIF/AVERAGEIF it is 1.
    /// </para>
    /// </summary>
    private static (int argIndex, RangeValue criteriaArray)? FindArrayCriteriaArg(
        IReadOnlyList<ScalarValue> args,
        int firstCriteriaArgIndex,
        int criteriaArgStep)  // 2 for *IFS (interleaved range,criteria), 1 for *IF
    {
        for (int i = firstCriteriaArgIndex; i < args.Count; i += criteriaArgStep)
        {
            if (args[i] is RangeValue rv)
                return (i, rv);
        }
        return null;
    }

    /// <summary>
    /// Substitute <paramref name="replacement"/> at position
    /// <paramref name="argIndex"/> in <paramref name="args"/> and return the new
    /// list. All other elements are shared (no copy of their values).
    /// </summary>
    private static IReadOnlyList<ScalarValue> ReplaceArg(
        IReadOnlyList<ScalarValue> args,
        int argIndex,
        ScalarValue replacement)
    {
        var copy = new ScalarValue[args.Count];
        for (int i = 0; i < args.Count; i++)
            copy[i] = i == argIndex ? replacement : args[i];
        return copy;
    }

    // ── SUMIF ──────────────────────────────────────────────────────────────────

    private static ScalarValue Sumif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        // Excel requires the range argument to be a genuine worksheet reference, not a
        // computed/array-constant value (e.g. {1,2,3} or TRANSPOSE(A1:A3)) — it returns
        // #VALUE! for those. RangeValue.IsSheetReference distinguishes the two (see the
        // same convention in BuiltInFunctions.Database.cs / BuiltInFunctions.Subtotal.cs).
        if (args[0] is not RangeValue { IsSheetReference: true } rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;

        // Array-criteria: criteria is a range → return one result per element.
        // Check before validating the optional sum-range so the expansion path
        // handles its own validation per element.
        if (criteria is RangeValue criteriaArray)
            return ExpandConditionalArrayCriteria(criteriaArray, args, 1, ctx, Sumif);

        if (args.Count > 2 && args[2] is ErrorValue sumRangeError) return sumRangeError;
        if (args.Count > 2 && args[2] is not RangeValue { IsSheetReference: true }) return ErrorValue.Value;

        RangeValue? sumRange = args.Count > 2 ? (RangeValue)args[2] : null;
        var criteriaMatcher = CompileCriteria(criteria);

        double total = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (criteriaMatcher.Matches(CellAtFlatIndex(rangeArg, i)))
            {
                var sv = sumRange is not null
                    ? CellAtRelativeOffsetOrContext(sumRange, i / rangeArg.ColCount, i % rangeArg.ColCount, ctx)
                    : CellAtFlatIndex(rangeArg, i);
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) total += value;
                else if (sv is BlankValue) { /* skip */ }
            }
        }
        return NumberResult(total);
    }

    // ── COUNTIF ────────────────────────────────────────────────────────────────

    private static ScalarValue Countif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        // Excel requires the range argument to be a genuine worksheet reference (see Sumif above).
        if (args[0] is not RangeValue { IsSheetReference: true } rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;

        // Array-criteria: criteria is a range → return one result per element.
        if (criteria is RangeValue criteriaArray)
            return ExpandConditionalArrayCriteria(criteriaArray, args, 1, ctx, Countif);

        var criteriaMatcher = CompileCriteria(criteria);
        return new NumberValue(CountMatchingCells(rangeArg, criteriaMatcher));
    }

    // ── AVERAGEIF ──────────────────────────────────────────────────────────────

    private static ScalarValue Averageif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        // Excel requires the range argument to be a genuine worksheet reference (see Sumif above).
        if (args[0] is not RangeValue { IsSheetReference: true } rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;

        // Array-criteria: criteria is a range → return one result per element.
        if (criteria is RangeValue criteriaArray)
            return ExpandConditionalArrayCriteria(criteriaArray, args, 1, ctx, Averageif);

        if (args.Count > 2 && args[2] is ErrorValue avgRangeError) return avgRangeError;
        if (args.Count > 2 && args[2] is not RangeValue { IsSheetReference: true }) return ErrorValue.Value;

        RangeValue? avgRange = args.Count > 2 ? (RangeValue)args[2] : null;
        var criteriaMatcher = CompileCriteria(criteria);

        double total = 0;
        int count = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (criteriaMatcher.Matches(CellAtFlatIndex(rangeArg, i)))
            {
                var sv = avgRange is not null
                    ? CellAtRelativeOffsetOrContext(avgRange, i / rangeArg.ColCount, i % rangeArg.ColCount, ctx)
                    : CellAtFlatIndex(rangeArg, i);
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    // ── SUMIFS ─────────────────────────────────────────────────────────────────

    private static ScalarValue Sumifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue sumRangeError) return sumRangeError;
        if (args[0] is not RangeValue { IsSheetReference: true } sumRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;

        // Array-criteria: any criteria-value slot holds a range → expand element-wise. Multiple
        // array criteria broadcast together into one matrix. Criteria-value slots for SUMIFS are at
        // indices 2, 4, 6, … (step 2).
        var arrayCriteriaArgs = FindAllArrayCriteriaArgs(args, firstCriteriaArgIndex: 2, criteriaArgStep: 2);
        if (arrayCriteriaArgs.Count > 0)
            return ExpandConditionalArrayCriteriaMulti(args, arrayCriteriaArgs, ctx, Sumifs);

        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, sumRange, out var criteriaSet) is { } pairError)
            return pairError;

        double total = 0;
        for (int r = 0; r < sumRange.RowCount; r++)
        {
            for (int c = 0; c < sumRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var sumValue = sumRange.Cells[r, c];
                if (sumValue is ErrorValue e) return e;
                if (TryCellNumber(sumValue, out double value)) total += value;
            }
        }
        return NumberResult(total);
    }

    // ── COUNTIFS ───────────────────────────────────────────────────────────────

    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;

        // Array-criteria: criteria-value slots for COUNTIFS are at indices 1, 3, 5, … (step 2).
        var arrayCriteriaArgs = FindAllArrayCriteriaArgs(args, firstCriteriaArgIndex: 1, criteriaArgStep: 2);
        if (arrayCriteriaArgs.Count > 0)
            return ExpandConditionalArrayCriteriaMulti(args, arrayCriteriaArgs, ctx, Countifs);

        int pairCount = args.Count / 2;
        if (TryCreateConditionalCriteriaSet(args, 0, pairCount, null, out var criteriaSet) is { } pairError)
            return pairError;

        int count = 0;
        var shapeRange = criteriaSet.ShapeRange;
        for (int r = 0; r < shapeRange.RowCount; r++)
        {
            for (int c = 0; c < shapeRange.ColCount; c++)
            {
                if (criteriaSet.Includes(r, c)) count++;
            }
        }
        return new NumberValue(count);
    }

    // ── AVERAGEIFS ─────────────────────────────────────────────────────────────

    private static ScalarValue Averageifs2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue avgRangeError) return avgRangeError;
        if (args[0] is not RangeValue { IsSheetReference: true } avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;

        // Array-criteria: criteria-value slots for AVERAGEIFS are at indices 2, 4, 6, … (step 2).
        var arrayCriteriaArgs = FindAllArrayCriteriaArgs(args, firstCriteriaArgIndex: 2, criteriaArgStep: 2);
        if (arrayCriteriaArgs.Count > 0)
            return ExpandConditionalArrayCriteriaMulti(args, arrayCriteriaArgs, ctx, Averageifs2);

        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, avgRange, out var criteriaSet) is { } pairError)
            return pairError;

        double total = 0;
        int count = 0;
        for (int r = 0; r < avgRange.RowCount; r++)
        {
            for (int c = 0; c < avgRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var avgValue = avgRange.Cells[r, c];
                if (avgValue is ErrorValue e) return e;
                if (TryCellNumber(avgValue, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    // ── MAXIFS ─────────────────────────────────────────────────────────────────

    // R97-union-deferred-backlog: MAXIFS/MINIFS (like SUMIFS/COUNTIFS/AVERAGEIFS) pair max_range
    // with one or more criteria_range arguments that must be the EXACT SAME SHAPE, then evaluate
    // element-by-element at matching (r,c) positions (TryCreateConditionalCriteriaSet below). A
    // parenthesized union argument (e.g. (A1:A5,C1:C5)) is deliberately NOT in
    // FormulaEvaluator.FunctionClassification.cs's UnionMaterializableRangeFunctions for these:
    // MaterializeUnionRangeValue collapses every area into one synthetic Nx1 column, which would
    // only be shape-safe if EVERY range argument (max_range AND every criteria_range) were
    // independently materialized from an IDENTICALLY-shaped union (same area boundaries, same
    // area order) -- the per-argument choke point in FormulaEvaluator.Functions.cs has no way to
    // enforce or even detect that cross-argument agreement, so materializing one argument at a
    // time (the mechanism every other UnionMaterializableRangeFunctions member relies on) could
    // silently misalign max_range's synthetic row N against a same-shaped-only-by-coincidence
    // criteria_range's row N belonging to a different original area. Real per-area pairing would
    // need a bespoke union-aware loop (materialize only when every argument's union has matching
    // area shapes, else #VALUE!) -- out of scope for the shared choke point.
    // A union max_range/criteria_range argument therefore still reaches this function body as a
    // raw UnionValue (not a RangeValue), which the `args[0] is not RangeValue` guard below turns
    // into #VALUE!. This engine has no live-Excel access to reverify interactively here, but it
    // matches Microsoft's documented constraint that every criteria_range must be the "same size
    // and shape" as max_range/sum_range -- a bare multi-area union has no single well-defined
    // shape to compare against, so #VALUE! is the Excel-consistent outcome pending a real-Excel
    // spot-check. See R97_MaxifsMinifsUnionDeferredTests for the pinned current behavior.
    private static ScalarValue Maxifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue maxRangeError) return maxRangeError;
        if (args[0] is not RangeValue { IsSheetReference: true } maxRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;

        // Array-criteria: criteria-value slots for MAXIFS are at indices 2, 4, 6, … (step 2).
        var arrayCriteriaArgs = FindAllArrayCriteriaArgs(args, firstCriteriaArgIndex: 2, criteriaArgStep: 2);
        if (arrayCriteriaArgs.Count > 0)
            return ExpandConditionalArrayCriteriaMulti(args, arrayCriteriaArgs, ctx, Maxifs);

        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, maxRange, out var criteriaSet) is { } pairError)
            return pairError;

        double best = 0;
        bool found = false;
        for (int r = 0; r < maxRange.RowCount; r++)
        {
            for (int c = 0; c < maxRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var maxValue = maxRange.Cells[r, c];
                if (maxValue is ErrorValue e) return e;
                if (TryCellNumber(maxValue, out double value))
                {
                    if (!found || value > best) best = value;
                    found = true;
                }
            }
        }
        return NumberResult(best);
    }

    // ── MINIFS ─────────────────────────────────────────────────────────────────

    private static ScalarValue Minifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue minRangeError) return minRangeError;
        if (args[0] is not RangeValue { IsSheetReference: true } minRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;

        // Array-criteria: criteria-value slots for MINIFS are at indices 2, 4, 6, … (step 2).
        var arrayCriteriaArgs = FindAllArrayCriteriaArgs(args, firstCriteriaArgIndex: 2, criteriaArgStep: 2);
        if (arrayCriteriaArgs.Count > 0)
            return ExpandConditionalArrayCriteriaMulti(args, arrayCriteriaArgs, ctx, Minifs);

        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, minRange, out var criteriaSet) is { } pairError)
            return pairError;

        double best = 0;
        bool found = false;
        for (int r = 0; r < minRange.RowCount; r++)
        {
            for (int c = 0; c < minRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var minValue = minRange.Cells[r, c];
                if (minValue is ErrorValue e) return e;
                if (TryCellNumber(minValue, out double value))
                {
                    if (!found || value < best) best = value;
                    found = true;
                }
            }
        }
        return NumberResult(best);
    }

    // ── Array-criteria expansion ────────────────────────────────────────────────

    /// <summary>
    /// Iterate over every element of <paramref name="criteriaArray"/>, substitute
    /// it at <paramref name="criteriaArgIndex"/> in <paramref name="args"/>, call
    /// <paramref name="scalarFunc"/>, and collect results into a RangeValue of the
    /// same shape.  Used by all *IF(S) functions when their criteria argument is a
    /// multi-cell range.
    /// </summary>
    private static ScalarValue ExpandConditionalArrayCriteria(
        RangeValue criteriaArray,
        IReadOnlyList<ScalarValue> args,
        int criteriaArgIndex,
        IEvalContext ctx,
        Func<IReadOnlyList<ScalarValue>, IEvalContext, ScalarValue> scalarFunc)
    {
        int rows = criteriaArray.RowCount;
        int cols = criteriaArray.ColCount;
        var resultCells = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var scalarCriteria = criteriaArray.Cells[r, c];
                var substitutedArgs = ReplaceArg(args, criteriaArgIndex, scalarCriteria);
                resultCells[r, c] = scalarFunc(substitutedArgs, ctx);
            }
        }
        return new RangeValue(resultCells);
    }

    /// <summary>
    /// Find every criteria-value slot (starting at <paramref name="firstCriteriaArgIndex"/>, stepping
    /// by <paramref name="criteriaArgStep"/>) that holds a RangeValue. These array criteria broadcast
    /// together into a single result instead of nesting.
    /// </summary>
    private static List<int> FindAllArrayCriteriaArgs(
        IReadOnlyList<ScalarValue> args,
        int firstCriteriaArgIndex,
        int criteriaArgStep)
    {
        var indexes = new List<int>();
        for (int i = firstCriteriaArgIndex; i < args.Count; i += criteriaArgStep)
        {
            if (args[i] is RangeValue)
                indexes.Add(i);
        }
        return indexes;
    }

    /// <summary>
    /// Expand a *IFS function that has one or more array-criteria slots. Excel broadcasts the array
    /// criteria together: a 2x1 criterion and a 1x3 criterion produce a 2x3 result. Each result cell
    /// substitutes the per-cell scalar for every array-criteria slot and calls the scalar function,
    /// so multiple array criteria yield one flat matrix rather than nested ranges.
    /// </summary>
    private static ScalarValue ExpandConditionalArrayCriteriaMulti(
        IReadOnlyList<ScalarValue> args,
        IReadOnlyList<int> criteriaArgIndexes,
        IEvalContext ctx,
        Func<IReadOnlyList<ScalarValue>, IEvalContext, ScalarValue> scalarFunc)
    {
        int rows = 1, cols = 1;
        foreach (var idx in criteriaArgIndexes)
        {
            var array = (RangeValue)args[idx];
            if (!CanBroadcastDimension(rows, array.RowCount) || !CanBroadcastDimension(cols, array.ColCount))
                return ErrorValue.Value;
            rows = Math.Max(rows, array.RowCount);
            cols = Math.Max(cols, array.ColCount);
        }

        var resultCells = new ScalarValue[rows, cols];
        var buffer = new ScalarValue[args.Count];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < args.Count; i++)
                    buffer[i] = args[i];
                foreach (var idx in criteriaArgIndexes)
                {
                    var array = (RangeValue)args[idx];
                    buffer[idx] = array.Cells[array.RowCount == 1 ? 0 : r, array.ColCount == 1 ? 0 : c];
                }
                resultCells[r, c] = scalarFunc(buffer.ToArray(), ctx);
            }
        }
        return new RangeValue(resultCells);
    }

    private static bool CanBroadcastDimension(int a, int b) => a == b || a == 1 || b == 1;

    private readonly struct ConditionalCriteriaPair
    {
        public ConditionalCriteriaPair(RangeValue range, CriteriaMatcher criteria)
        {
            Range = range;
            Criteria = criteria;
        }

        public RangeValue Range { get; }
        public CriteriaMatcher Criteria { get; }
    }

    private readonly struct ConditionalCriteriaSet
    {
        private readonly ConditionalCriteriaPair[] _pairs;

        public ConditionalCriteriaSet(RangeValue shapeRange, ConditionalCriteriaPair[] pairs)
        {
            ShapeRange = shapeRange;
            _pairs = pairs;
        }

        public RangeValue ShapeRange { get; }

        public bool Includes(int row, int col)
        {
            for (int i = 0; i < _pairs.Length; i++)
            {
                var pair = _pairs[i];
                if (!pair.Criteria.Matches(pair.Range.Cells[row, col]))
                    return false;
            }

            return true;
        }
    }

    private static ErrorValue? TryCreateConditionalCriteriaSet(
        IReadOnlyList<ScalarValue> args,
        int firstCriteriaRangeIndex,
        int pairCount,
        RangeValue? requiredShape,
        out ConditionalCriteriaSet criteriaSet)
    {
        criteriaSet = default;
        var pairs = new ConditionalCriteriaPair[pairCount];
        var shapeRange = requiredShape;

        for (int p = 0; p < pairCount; p++)
        {
            int rangeIndex = firstCriteriaRangeIndex + p * 2;
            int criteriaIndex = rangeIndex + 1;

            if (args[rangeIndex] is ErrorValue rangeError) return rangeError;
            if (args[rangeIndex] is not RangeValue { IsSheetReference: true } criteriaRange) return ErrorValue.Value;

            shapeRange ??= criteriaRange;
            if (!SameShape(shapeRange, criteriaRange)) return ErrorValue.Value;

            var criteria = args[criteriaIndex];
            if (criteria is ErrorValue criteriaError) return criteriaError;
            pairs[p] = new ConditionalCriteriaPair(criteriaRange, CompileCriteria(criteria));
        }

        criteriaSet = new ConditionalCriteriaSet(shapeRange!, pairs);
        return null;
    }

    private static int CountMatchingCells(RangeValue range, CriteriaMatcher criteria)
    {
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                if (criteria.Matches(range.Cells[r, c]))
                    count++;
        return count;
    }

    private static int FlatCount(RangeValue range) => range.RowCount * range.ColCount;

    private static ScalarValue CellAtFlatIndex(RangeValue range, int index)
    {
        int row = index / range.ColCount;
        int col = index - (row * range.ColCount);
        return range.Cells[row, col];
    }

    private static ScalarValue CellAtRelativeOffsetOrContext(RangeValue range, int rowOffset, int colOffset, IEvalContext ctx)
    {
        if (rowOffset < range.RowCount && colOffset < range.ColCount)
            return range.Cells[rowOffset, colOffset];

        var row = range.StartRow + (uint)rowOffset;
        var col = range.StartCol + (uint)colOffset;
        return !string.IsNullOrEmpty(range.SheetName)
            ? ctx.GetCellValue(range.SheetName, row, col)
            : ctx.GetCellValue(row, col);
    }
}
