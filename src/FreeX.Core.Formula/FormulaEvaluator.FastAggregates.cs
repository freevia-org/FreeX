using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private static bool TryEvaluateRangeOnlyFastAggregate(
        string functionName,
        IReadOnlyList<FormulaNode> arguments,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (!TryGetFastAggregateKind(functionName, out var kind))
            return false;

        var ranges = new List<FastAggregateRange>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var resolution = TryResolveFastAggregateRange(kind, arguments[index], context, out var range, out var error);
            if (resolution == FastAggregateRangeResolution.Unsupported)
                return false;

            if (error is not null)
            {
                if (ranges.Count > 0 &&
                    TryFindFastRangeOnlyImmediateError(kind, ranges, context, out var priorError))
                    result = priorError;
                else
                    result = error;

                return true;
            }

            ranges.Add(range);
        }

        result = EvaluateFastRangeOnlyAggregate(kind, ranges, context);
        return true;
    }

    private static ScalarValue EvaluateFastRangeOnlyAggregate(
        FastAggregateKind kind,
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context)
    {
        return kind switch
        {
            FastAggregateKind.Sum => EvaluateFastRangeOnlySum(ranges, context),
            FastAggregateKind.Average => EvaluateFastRangeOnlyAverage(ranges, context),
            FastAggregateKind.Min => EvaluateFastRangeOnlyMinMax(ranges, context, findMax: false),
            FastAggregateKind.Max => EvaluateFastRangeOnlyMinMax(ranges, context, findMax: true),
            FastAggregateKind.CountBlank => EvaluateFastRangeOnlyCountBlank(ranges, context),
            FastAggregateKind.StdevS => EvaluateFastRangeOnlyVariance(ranges, context, sample: true, squareRoot: true),
            FastAggregateKind.StdevP => EvaluateFastRangeOnlyVariance(ranges, context, sample: false, squareRoot: true),
            FastAggregateKind.VarS => EvaluateFastRangeOnlyVariance(ranges, context, sample: true, squareRoot: false),
            FastAggregateKind.VarP => EvaluateFastRangeOnlyVariance(ranges, context, sample: false, squareRoot: false),
            _ => EvaluateFastRangeOnlyCount(ranges, context)
        };
    }

    private static bool TryFindFastRangeOnlyImmediateError(
        FastAggregateKind kind,
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        out ErrorValue error)
    {
        error = null!;
        if (kind is FastAggregateKind.Count or FastAggregateKind.CountBlank)
            return false;

        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var cellValue = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        _ = TryDirectRangeNumber(cellValue, out _, out var cellError);
                        if (cellError is not null)
                        {
                            error = cellError;
                            return true;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        _ = TryDirectRangeNumber(value, out _, out var cellError);
                        if (cellError is not null)
                        {
                            error = cellError;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static ScalarValue EvaluateFastRangeOnlySum(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        double total = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs / RoundTo15SignificantDigits) so this
        // range-only SUM fast path stays consistent with the general BuiltInFunctions.Sum path.
        return double.IsFinite(total) ? NumberValueFor(RoundTo15SignificantDigits(total)) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyAverage(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        double total = 0;
        long count = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                            count++;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                            count++;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        if (count == 0) return ErrorValue.DivByZero;

        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs / RoundTo15SignificantDigits), the
        // same way the range-only SUM fast path above does, so this range-only AVERAGE fast path
        // stays consistent with both EvaluateFastRangeOnlySum and the general BuiltInFunctions
        // Average() path.
        double roundedTotal = RoundTo15SignificantDigits(total);
        double quotient = roundedTotal / count;
        return double.IsFinite(quotient) ? NumberValueFor(RoundTo15SignificantDigits(quotient)) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyMinMax(
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        bool findMax)
    {
        double? result = null;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            if (result is null ||
                                (findMax ? number > result.Value : number < result.Value))
                            {
                                result = number;
                            }
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            if (result is null ||
                                (findMax ? number > result.Value : number < result.Value))
                            {
                                result = number;
                            }
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        return result is null
            ? NumberValueFor(0)
            : double.IsFinite(result.Value) ? NumberValueFor(result.Value) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyCount(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        long count = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        if (value is NumberValue or DateTimeValue)
                            count++;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (value is NumberValue or DateTimeValue)
                            count++;
                    }
                }
            }
        }

        return NumberValueFor(count);
    }

    private static ScalarValue EvaluateFastRangeOnlyCountBlank(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        long count = 0;
        foreach (var range in ranges)
        {
            // Only the used-range-clamped Start/End rectangle (never the un-clamped nominal
            // full-column/full-row extent) is actually scanned here, so a full-sheet
            // COUNTBLANK(A:XFD) never iterates billions of cells. Everything counted below is
            // the number of NON-blank cells found inside that scanned rectangle.
            long nonBlank = 0;
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);

                        if (value is not (BlankValue or TextValue { Value.Length: 0 }))
                            nonBlank++;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);

                        if (value is not (BlankValue or TextValue { Value.Length: 0 }))
                            nonBlank++;
                    }
                }
            }

            // range.NominalCellCount carries the un-clamped nominal cell count when the
            // rectangle above was narrowed from a full-column/full-row (or full-span
            // named-range) extent down to the sheet's used range (see
            // TryResolveFastAggregateRange). Every nominal cell outside that used-range-clamped
            // rectangle is guaranteed blank (Sheet.GetUsedRange's bounding box covers every
            // populated/formatted cell), so blanks = nominal cells - non-blank cells found
            // inside the scanned rectangle. For an ordinary bounded range NominalCellCount is
            // null, and the scanned rectangle already covers every cell that needs counting.
            var totalCells = range.NominalCellCount
                ?? FormulaSafetyLimits.GetRangeCellCount(range.StartRow, range.StartCol, range.EndRow, range.EndCol);
            count += totalCells - nonBlank;
        }

        return NumberValueFor(count);
    }

    private static ScalarValue EvaluateFastRangeOnlyVariance(
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        bool sample,
        bool squareRoot)
    {
        long count = 0;
        double mean = 0;
        double m2 = 0;

        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                // A null sheet here means range.SheetName is an external-workbook reference
                // (already validated by IEvalContext.SheetExists when the range was resolved),
                // not a missing sheet; GetCellValue's external fallback resolves it instead of
                // wrongly reporting #REF!.
                var sheet = ResolveFastAggregateSheet(range, sheetContext);

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet is not null
                            ? sheet.GetValue(row, col)
                            : context.GetCellValue(range.SheetName!, row, col);
                        if (!AccumulateFastVarianceValue(value, ref count, ref mean, ref m2, out var error))
                            return error!;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (!AccumulateFastVarianceValue(value, ref count, ref mean, ref m2, out var error))
                            return error!;
                    }
                }
            }
        }

        if (count == 0 || (sample && count < 2))
            return ErrorValue.DivByZero;

        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs / RoundTo15SignificantDigits), the
        // same way EvaluateFastRangeOnlySum/EvaluateFastRangeOnlyAverage above do, so this
        // range-only STDEV/VAR fast path stays consistent with the general BuiltInFunctions
        // Stdev()/VarS()/VarP() path.
        var variance = RoundTo15SignificantDigits(m2 / (sample ? count - 1 : count));
        var result = squareRoot ? Math.Sqrt(variance) : variance;
        return double.IsFinite(result) ? NumberValueFor(result) : ErrorValue.Num;
    }

    private static bool AccumulateFastVarianceValue(
        ScalarValue value,
        ref long count,
        ref double mean,
        ref double m2,
        out ErrorValue? error)
    {
        if (!TryDirectRangeNumber(value, out var number, out error))
            return error is null;

        count++;
        var delta = number - mean;
        mean += delta / count;
        m2 += delta * (number - mean);
        return true;
    }

    private static Sheet? ResolveFastAggregateSheet(FastAggregateRange range, SheetEvalContext context)
        => context.ResolveSheetForFastRange(range.SheetName);

    // Intersect a full-column/full-row range with the target sheet's used (populated) extent.
    // Returns false when there is nothing to aggregate (empty sheet or no overlap), in which
    // case the caller should treat the range as containing zero cells. When the context cannot
    // resolve a sheet (non-sheet context), the range is left unchanged.
    private static bool TryClampFullRangeToUsed(
        string? sheetName,
        IEvalContext context,
        ref uint startRow,
        ref uint startCol,
        ref uint endRow,
        ref uint endCol)
    {
        if (context is not SheetEvalContext sheetContext)
            return true;

        var sheet = sheetContext.ResolveSheetForFastRange(sheetName);
        if (sheet is null)
            return true;

        if (sheet.GetUsedRange() is not { } used)
            return false;

        var clampedStartRow = Math.Max(startRow, used.Start.Row);
        var clampedEndRow = Math.Min(endRow, used.End.Row);
        var clampedStartCol = Math.Max(startCol, used.Start.Col);
        var clampedEndCol = Math.Min(endCol, used.End.Col);

        if (clampedStartRow > clampedEndRow || clampedStartCol > clampedEndCol)
            return false;

        startRow = clampedStartRow;
        endRow = clampedEndRow;
        startCol = clampedStartCol;
        endCol = clampedEndCol;
        return true;
    }

    private static FastAggregateRangeResolution TryResolveFastAggregateRange(
        FastAggregateKind kind,
        FormulaNode argument,
        IEvalContext context,
        out FastAggregateRange range,
        out ErrorValue? error)
    {
        range = default;
        error = null;

        if (TryAsRangeRef(argument, out var rangeRef))
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
            {
                error = ErrorValue.Ref;
                return FastAggregateRangeResolution.Error;
            }

            var startRow = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
            var startCol = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
            var endRow = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
            var endCol = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);

            // Full-column (A:C) / full-row (1:5) ranges nominally span 1,048,576 rows or
            // 16,384 columns. Excel aggregates only the populated extent; clamping to the
            // sheet's used range gives the same numeric result, keeps us under the streaming
            // cap (so e.g. SUM(A:C) no longer wrongly returns #REF!), and is far faster.
            // For CountBlank, the un-clamped nominal cell count is preserved separately (see
            // FastAggregateRange.NominalCellCount) so it can still count blanks across the
            // whole nominal range without ever iterating past the used-range-clamped extent.
            //
            // The clamp is applied for ANY range shape, not just full-column/full-row: an
            // ordinary explicit BOUNDED range (e.g. A1:J200000 -- 10 cols x 200,000 rows =
            // 2,000,000 cells, well inside Excel's real 1,048,576-row/16,384-col sheet limits)
            // must scan only its used-range intersection too, or a mostly-empty large range
            // would be wrongly rejected by the streaming cap below even though the underlying
            // accumulator never materializes anything and the actual scan is tiny. Intersecting
            // with the used range can only ever SHRINK the queried rectangle (Math.Max/Math.Min
            // against the requested bounds), so it never drops populated cells the caller asked
            // for -- it only skips cells that are provably blank.
            long? nominalCellCount = null;
            if (kind == FastAggregateKind.CountBlank)
                nominalCellCount = FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);

            if (!TryClampFullRangeToUsed(rangeRef.SheetName, context, ref startRow, ref startCol, ref endRow, ref endCol))
            {
                // No populated cells overlap the range: emit an empty range (endRow < startRow
                // so every aggregate loop iterates zero cells -> SUM/COUNT 0, AVERAGE #DIV/0!, etc.).
                range = new FastAggregateRange(rangeRef.SheetName, 1, 1, 0, 0, nominalCellCount);
                return FastAggregateRangeResolution.Range;
            }

            var resolvedRange = new FastAggregateRange(rangeRef.SheetName, startRow, startCol, endRow, endCol, nominalCellCount);

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        if (argument is FunctionCallNode { FunctionName: "INDIRECT" } indirect)
        {
            if (!TryBuildLiteralIndirectArguments(indirect, out var indirectArgs, out error))
                return error is null
                    ? FastAggregateRangeResolution.Unsupported
                    : FastAggregateRangeResolution.Error;

            if (!BuiltInFunctions.TryResolveIndirectRangeReference(indirectArgs, context, out var indirectRange, out var indirectError))
            {
                error = indirectError as ErrorValue;
                return error is null
                    ? FastAggregateRangeResolution.Unsupported
                    : FastAggregateRangeResolution.Error;
            }

            var startRow = Math.Min(indirectRange.StartRow, indirectRange.EndRow);
            var startCol = Math.Min(indirectRange.StartCol, indirectRange.EndCol);
            var endRow = Math.Max(indirectRange.StartRow, indirectRange.EndRow);
            var endCol = Math.Max(indirectRange.StartCol, indirectRange.EndCol);

            long? nominalCellCount = null;
            if (kind == FastAggregateKind.CountBlank)
                nominalCellCount = FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);

            // Clamp for ANY range shape, not just full-column/full-row -- see the matching
            // comment on the literal-range path above for why an ordinary bounded range (e.g.
            // INDIRECT("A1:J200000")) needs the same used-range intersection.
            if (!TryClampFullRangeToUsed(indirectRange.SheetName, context, ref startRow, ref startCol, ref endRow, ref endCol))
            {
                range = new FastAggregateRange(indirectRange.SheetName, 1, 1, 0, 0, nominalCellCount);
                return FastAggregateRangeResolution.Range;
            }

            var resolvedRange = new FastAggregateRange(
                indirectRange.SheetName,
                startRow,
                startCol,
                endRow,
                endCol,
                nominalCellCount);

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        if (argument is NamedRangeNode named)
        {
            if (context.TryResolveLambdaBinding(named.Name) is not null)
                return FastAggregateRangeResolution.Unsupported;

            // Excel scope precedence: a sheet-scoped named FORMULA outranks a same-named
            // workbook-global named RANGE. Bail out to the general (slow) argument-expansion
            // path in FormulaEvaluator.Functions.cs, which resolves that precedence correctly,
            // instead of streaming the wrong (global range) cells here.
            if (IsSheetScopedName(named.Name, context, out var sheetScopedIsFormula) && sheetScopedIsFormula)
                return FastAggregateRangeResolution.Unsupported;

            var resolvedNamedRange = context.TryResolveNamedRange(named.Name);
            if (resolvedNamedRange is null)
                return FastAggregateRangeResolution.Unsupported;

            var gridRange = resolvedNamedRange.Value;
            var sheetName = context.TryGetSheetName(gridRange.Start.Sheet);

            var startRow = gridRange.Start.Row;
            var startCol = gridRange.Start.Col;
            var endRow = gridRange.End.Row;
            var endCol = gridRange.End.Col;

            // Apply the same used-range clamp that literal range and INDIRECT paths use, for ANY
            // range shape -- not just full-column/full-row. A named range like =Data where
            // Data=$A:$B spans all 1,048,576 rows and would exceed MaxStreamingRangeCells without
            // this clamp; an ordinary bounded named range (e.g. Data=$A$1:$J$200000) needs the
            // same used-range intersection for the same reason the literal-range path does.
            long? nominalCellCount = null;
            if (kind == FastAggregateKind.CountBlank)
                nominalCellCount = FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);

            if (!TryClampFullRangeToUsed(sheetName, context, ref startRow, ref startCol, ref endRow, ref endCol))
            {
                range = new FastAggregateRange(sheetName, 1, 1, 0, 0, nominalCellCount);
                return FastAggregateRangeResolution.Range;
            }

            var resolvedRange = new FastAggregateRange(sheetName, startRow, startCol, endRow, endCol, nominalCellCount);

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        return FastAggregateRangeResolution.Unsupported;
    }

    private static bool TryAcceptFastAggregateRange(FastAggregateRange range, FastAggregateKind kind, out ErrorValue? error)
    {
        error = null;

        // COUNTBLANK now scans only the used-range-clamped Start/End rectangle (see
        // TryResolveFastAggregateRange), never the un-clamped nominal full-column/full-row
        // extent, so the same streaming cap that guards SUM/AVERAGE/etc. applies to the
        // rectangle it actually iterates.
        var cellCount = FormulaSafetyLimits.GetRangeCellCount(
            range.StartRow,
            range.StartCol,
            range.EndRow,
            range.EndCol);

        // Stdev/Var kinds are also pure streaming Welford accumulators (no materialization;
        // see EvaluateFastRangeOnlyVariance), so they use the same streaming cap as
        // SUM/AVERAGE/etc. rather than the lower materialized-range cap.
        if (cellCount <= FormulaSafetyLimits.MaxStreamingRangeCells)
            return true;

        error = ErrorValue.Ref;
        return false;
    }

    private static bool TryBuildLiteralIndirectArguments(
        FunctionCallNode node,
        out IReadOnlyList<ScalarValue> args,
        out ErrorValue? error)
    {
        args = [];
        error = null;
        if (node.Arguments.Count is < 1 or > 2)
        {
            error = ErrorValue.Value;
            return false;
        }

        if (!TryBuildLiteralIndirectArgument(node.Arguments[0], out var refText, out error))
            return false;

        if (node.Arguments.Count == 1)
        {
            args = [refText];
            return true;
        }

        if (!TryBuildLiteralIndirectArgument(node.Arguments[1], out var useA1, out error))
            return false;

        args = [refText, useA1];
        return true;
    }

    private static bool TryBuildLiteralIndirectArgument(
        FormulaNode node,
        out ScalarValue value,
        out ErrorValue? error)
    {
        value = BlankValue.Instance;
        error = null;
        switch (node)
        {
            case StringNode text:
                value = new TextValue(text.Value);
                return true;
            case BooleanNode boolean:
                value = boolean.Value ? TrueValue : FalseValue;
                return true;
            case NumberNode number:
                value = NumberValueFor(number.Value);
                return true;
            case OmittedArgumentNode:
                value = BlankValue.Instance;
                return true;
            case ErrorNode errorNode:
                error = errorNode.Error;
                return false;
            default:
                return false;
        }
    }

    private static bool TryGetFastAggregateKind(string functionName, out FastAggregateKind kind)
    {
        switch (functionName)
        {
            case "SUM":
                kind = FastAggregateKind.Sum;
                return true;
            case "AVERAGE":
                kind = FastAggregateKind.Average;
                return true;
            case "MIN":
                kind = FastAggregateKind.Min;
                return true;
            case "MAX":
                kind = FastAggregateKind.Max;
                return true;
            case "COUNT":
                kind = FastAggregateKind.Count;
                return true;
            case "COUNTBLANK":
                kind = FastAggregateKind.CountBlank;
                return true;
            case "STDEV":
            case "STDEV.S":
                kind = FastAggregateKind.StdevS;
                return true;
            case "STDEVP":
            case "STDEV.P":
                kind = FastAggregateKind.StdevP;
                return true;
            case "VAR":
            case "VAR.S":
                kind = FastAggregateKind.VarS;
                return true;
            case "VARP":
            case "VAR.P":
                kind = FastAggregateKind.VarP;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private readonly record struct FastAggregateRange(
        string? SheetName,
        uint StartRow,
        uint StartCol,
        uint EndRow,
        uint EndCol,
        // Set only for CountBlank ranges that were narrowed from a full-column/full-row
        // (or full-span named-range) nominal extent down to the sheet's used-range for
        // scanning. Carries the un-clamped nominal cell count so EvaluateFastRangeOnlyCountBlank
        // can add back the cells outside the used range (all guaranteed blank) without ever
        // iterating them. Null for ordinary bounded ranges, where the scanned rectangle already
        // covers the whole range.
        long? NominalCellCount = null);

    private enum FastAggregateKind
    {
        Sum,
        Average,
        Min,
        Max,
        Count,
        CountBlank,
        StdevS,
        StdevP,
        VarS,
        VarP
    }

    private enum FastAggregateRangeResolution
    {
        Unsupported,
        Range,
        Error
    }

    private static bool TryDirectRangeNumber(ScalarValue value, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;
        switch (value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                return false;
        }
    }
}
