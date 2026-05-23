using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // SUBTOTAL
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Subtotal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var funcNumD = ToNumber(args[0]);
        if (!double.IsFinite(funcNumD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        bool skipHidden = funcNum >= 101;
        int baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;

        // Collect all numeric values from remaining args, respecting hidden-row exclusion
        var nums = new List<double>();
        int countaCount = 0;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue ei) return ei;
            if (args[i] is RangeValue rv)
            {
                for (int r = 0; r < rv.RowCount; r++)
                {
                    uint absRow = rv.StartRow + (uint)r;
                    if (skipHidden && ctx.IsRowHidden(absRow)) continue;
                    for (int c = 0; c < rv.ColCount; c++)
                    {
                        var cell = rv.Cells[r, c];
                        if (cell is ErrorValue err) return err;
                        if (TryCellNumber(cell, out double value)) nums.Add(value);
                        if (cell is not BlankValue) countaCount++;
                    }
                }
            }
            else if (TryCellNumber(args[i], out double scalarNum))
            {
                nums.Add(scalarNum);
                countaCount++;
            }
            else if (args[i] is not BlankValue)
            {
                countaCount++;
            }
        }

        return baseFunc switch
        {
            1  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Average()),
            2  => new NumberValue(nums.Count),
            3  => new NumberValue(countaCount),
            4  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Max()),
            5  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Min()),
            6  => NumberResult(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (acc, x) => acc * x)),
            7  => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevS(nums)),
            8  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevP(nums)),
            9  => NumberResult(nums.Sum()),
            10 => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalVarS(nums)),
            11 => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalVarP(nums)),
            _  => ErrorValue.Value
        };
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
