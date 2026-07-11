using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // SUBTOTAL
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Subtotal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetScalarControlArgument(args[0], out var funcNumArg, out var funcNumError)) return funcNumError;
        var funcNumD = ToNumber(funcNumArg);
        if (!double.IsFinite(funcNumD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        bool skipHidden = funcNum >= 101;
        int baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;

        var numeric = new SubtotalNumericAccumulator();
        List<double>? statisticalValues = IsSubtotalStatisticalFunction(baseFunc) ? [] : null;
        int countaCount = 0;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue ei) return ei;
            if (args[i] is RangeValue rv)
            {
                // A computed/virtual array (e.g. the result of FILTER, SORT, SEQUENCE, MAP, ...)
                // has no real position on the sheet, so RangeValue defaults its StartRow/StartCol
                // to 1 and leaves SheetName null (see RangeValue in FreeX.Core.Model/ScalarValue.cs
                // and its construction sites in BuiltInFunctions.DynamicArrays.*.cs). Only a genuine
                // worksheet REFERENCE — recognizable by a real SheetName, or a start row/col other
                // than that (1,1) default — carries coordinates meaningful enough to look up
                // hidden-row state or nested SUBTOTAL/AGGREGATE formulas. Applying those
                // coordinate-based exclusions to a virtual array's bogus (1,1)-anchored coordinates
                // silently (and wrongly) drops elements whenever sheet row 1 / column A happens to
                // be hidden or hold a nested SUBTOTAL, regardless of where the array's values
                // actually came from. See R19-formula-functions-edge-1.
                bool isReference = rv.SheetName is not null || rv.StartRow != 1 || rv.StartCol != 1;

                for (int r = 0; r < rv.RowCount; r++)
                {
                    uint absRow = rv.StartRow + (uint)r;
                    if (isReference && ShouldSkipSubtotalRow(ctx, rv, absRow, skipHidden)) continue;
                    for (int c = 0; c < rv.ColCount; c++)
                    {
                        uint absCol = rv.StartCol + (uint)c;
                        if (isReference && IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                        var cell = rv.Cells[r, c];
                        if (cell is ErrorValue err)
                        {
                            // COUNT (2) ignores error cells; COUNTA (3) counts them as non-blank.
                            // All other aggregating functions propagate the error immediately.
                            if (baseFunc == 2) continue;
                            if (baseFunc == 3) { countaCount++; continue; }
                            return err;
                        }
                        if (TryCellNumber(cell, out double value))
                        {
                            numeric.Add(value, baseFunc);
                            statisticalValues?.Add(value);
                        }
                        if (cell is not BlankValue) countaCount++;
                    }
                }
            }
            else if (TryCellNumber(args[i], out double scalarNum))
            {
                numeric.Add(scalarNum, baseFunc);
                statisticalValues?.Add(scalarNum);
                countaCount++;
            }
            else if (args[i] is not BlankValue)
            {
                countaCount++;
            }
        }

        return baseFunc switch
        {
            1  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(numeric.Average),
            2  => new NumberValue(numeric.Count),
            3  => new NumberValue(countaCount),
            // MAX/MIN return 0 for an all-non-numeric/empty range, matching the plain MAX()/MIN()
            // functions (BuiltInFunctions.StatisticalCore.Aggregates.cs) and real Excel — unlike
            // AVERAGE/STDEV/VAR (1,7,8,10,11) which genuinely error (#DIV/0!) on an empty sample.
            4  => NumberResult(numeric.Count == 0 ? 0 : numeric.Max),
            5  => NumberResult(numeric.Count == 0 ? 0 : numeric.Min),
            6  => NumberResult(numeric.Count == 0 ? 0 : numeric.Product),
            7  => numeric.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevS(statisticalValues!)),
            8  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevP(statisticalValues!)),
            9  => NumberResult(numeric.Sum),
            10 => numeric.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalVarS(statisticalValues!)),
            11 => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalVarP(statisticalValues!)),
            _  => ErrorValue.Value
        };
    }

    private static bool ShouldSkipSubtotalRow(IEvalContext ctx, RangeValue range, uint row, bool skipHidden)
    {
        return range.SheetName is null
            ? skipHidden ? ctx.IsRowHidden(row) : ctx.IsRowFilterHidden(row)
            : skipHidden ? ctx.IsRowHidden(range.SheetName, row) : ctx.IsRowFilterHidden(range.SheetName, row);
    }

    private static bool IsNestedSubtotalOrAggregateCell(IEvalContext ctx, RangeValue range, uint row, uint col)
    {
        var cell = range.SheetName is null
            ? ctx.TryGetCell(row, col)
            : ctx.TryGetCell(range.SheetName, row, col);
        return IsSubtotalOrAggregateFormula(cell?.FormulaText);
    }

    private static bool IsSubtotalOrAggregateFormula(string? formulaText)
    {
        if (string.IsNullOrWhiteSpace(formulaText)) return false;
        var text = formulaText.TrimStart();
        if (text.StartsWith("=", StringComparison.Ordinal)) text = text[1..].TrimStart();
        return text.StartsWith("SUBTOTAL(", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("AGGREGATE(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSubtotalStatisticalFunction(int baseFunc)
    {
        return baseFunc is 7 or 8 or 10 or 11;
    }

    private struct SubtotalNumericAccumulator
    {
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Product { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Average => Sum / Count;

        public void Add(double value, int baseFunc)
        {
            Count++;
            switch (baseFunc)
            {
                case 1:
                case 9:
                    Sum += value;
                    break;
                case 4:
                    Max = Count == 1 ? value : Math.Max(Max, value);
                    break;
                case 5:
                    Min = Count == 1 ? value : Math.Min(Min, value);
                    break;
                case 6:
                    Product = Count == 1 ? value : Product * value;
                    break;
            }
        }
    }

    private static double SubtotalVarS(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
    }

    private static double SubtotalVarP(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
    }

    private static double SubtotalStdDevS(List<double> nums) => Math.Sqrt(SubtotalVarS(nums));
    private static double SubtotalStdDevP(List<double> nums) => Math.Sqrt(SubtotalVarP(nums));

}
