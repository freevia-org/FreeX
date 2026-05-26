using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
                for (int r = 0; r < rv.RowCount; r++)
                {
                    uint absRow = rv.StartRow + (uint)r;
                    if (ShouldSkipSubtotalRow(ctx, rv, absRow, skipHidden)) continue;
                    for (int c = 0; c < rv.ColCount; c++)
                    {
                        uint absCol = rv.StartCol + (uint)c;
                        if (IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                        var cell = rv.Cells[r, c];
                        if (cell is ErrorValue err) return err;
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
            4  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(numeric.Max),
            5  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(numeric.Min),
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
