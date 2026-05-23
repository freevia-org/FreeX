using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A2 information and aggregate functions.

    // Defensive fallback if EvaluateAstAware routing is bypassed; the
    // FormulaEvaluator dispatches ISREF/ISFORMULA/FORMULATEXT/OFFSET to
    // AST-aware code paths before invoking this delegate.
    private static ScalarValue AstAwareStub(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => ErrorValue.Value;

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – CELL(info_type, [reference])
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue CellInfo(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var infoType = ToText(args[0]).Trim().ToLowerInvariant();

        // Resolve reference: use args[1] when present; otherwise default to A1.
        // We don't have access to the original AST node here, so we read the
        // computed scalar/range (built by the evaluator's standard arg expansion).
        uint row = 1, col = 1;
        ScalarValue cellValue = BlankValue.Instance;
        var sheet = ctx.CurrentSheet;
        if (args.Count >= 2)
        {
            if (args[1] is ErrorValue e1) return e1;
            if (args[1] is RangeValue rv)
            {
                row = rv.StartRow;
                col = rv.StartCol;
                cellValue = rv.Cells[0, 0];
            }
            else if (args[1] is BlankValue)
            {
                cellValue = ctx.GetCellValue(row, col);
            }
            else
            {
                // A non-range value — CELL needs a reference; treat as A1 of current sheet
                // but use the computed scalar as the value for "contents"/"type".
                cellValue = args[1];
            }
        }
        else
        {
            cellValue = ctx.GetCellValue(row, col);
        }

        var underlying = sheet?.GetCell(row, col);

        switch (infoType)
        {
            case "address":
                return new TextValue($"${CellAddress.NumberToColumnName(col)}${row}");
            case "col":
                return new NumberValue(col);
            case "row":
                return new NumberValue(row);
            case "contents":
                return cellValue;
            case "type":
                return new TextValue(cellValue switch
                {
                    BlankValue => "b",
                    TextValue  => "l",
                    _          => "v"
                });
            case "protect":
            {
                if (sheet is null) return new NumberValue(0);
                bool locked = true; // default style is locked
                if (underlying is not null && ctx.CurrentWorkbook is not null)
                {
                    var style = ctx.CurrentWorkbook.GetStyle(underlying.StyleId);
                    locked = style.Locked;
                }
                return new NumberValue(sheet.IsProtected && locked ? 1 : 0);
            }
            case "width":
            {
                if (sheet is null) return new NumberValue(8);
                if (sheet.ColumnWidths.TryGetValue(col, out var w)) return new NumberValue(w);
                return new NumberValue(sheet.DefaultColumnWidth);
            }
            case "filename":
                // In-memory workbook has no on-disk path; Excel compat is empty string.
                return new TextValue("");
            case "format":
            {
                if (underlying is null || ctx.CurrentWorkbook is null) return new TextValue("");
                var style = ctx.CurrentWorkbook.GetStyle(underlying.StyleId);
                return new TextValue(style.NumberFormat == "General" ? "" : style.NumberFormat);
            }
            case "color":
                return new NumberValue(0);
            case "parentheses":
                return new NumberValue(0);
            case "prefix":
                return new TextValue("");
            default:
                return ErrorValue.Value;
        }
    }


    private static ScalarValue InfoFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var infoType = ToText(args[0]).Trim().ToLowerInvariant();
        switch (infoType)
        {
            case "directory":
                try { return new TextValue(Environment.CurrentDirectory); }
                catch { return new TextValue(""); }
            case "numfile":
                return new NumberValue(ctx.CurrentWorkbook?.SheetCount ?? 1);
            case "origin":
                return new TextValue("$A:$A1");
            case "osversion":
                return new TextValue("Windows (32-bit) NT 10.00");
            case "recalc":
                return new TextValue(ctx.CurrentWorkbook?.CalculationMode == WorkbookCalculationMode.Manual
                    ? "Manual" : "Automatic");
            case "release":
                return new TextValue("16.0");
            case "system":
                return new TextValue("pcdos");
            case "memavail":
            case "memused":
            case "totmem":
                return new NumberValue(0);
            default:
                return ErrorValue.Value;
        }
    }

    private static ScalarValue Aggregate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var funcNumD = ToNumber(args[0]);
        var optionsD = ToNumber(args[1]);
        if (!double.IsFinite(funcNumD) || !double.IsFinite(optionsD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        int options = (int)optionsD;
        if (funcNum < 1 || funcNum > 19) return ErrorValue.Value;
        if (options < 0 || options > 7) return ErrorValue.Value;

        bool ignoreErrors = options == 2 || options == 3 || options == 6 || options == 7;
        // Hidden-row ignore (options 1, 3, 5, 7) is not honored here — see header note.

        bool needsK = funcNum is >= 14 and <= 19;
        if (needsK && args.Count < 4) return ErrorValue.Value;

        var nums = new List<double>();
        // Collect from positional value args (skip funcNum, options, and a potential k arg)
        int kIndex = needsK ? args.Count - 1 : -1;
        for (int i = 2; i < args.Count; i++)
        {
            if (i == kIndex) continue;
            var arg = args[i];
            if (arg is ErrorValue err)
            {
                if (ignoreErrors) continue;
                return err;
            }
            if (arg is RangeValue rv)
            {
                foreach (var cell in rv.Flatten())
                {
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (TryCellNumber(cell, out double v)) nums.Add(v);
                }
            }
            else if (TryCellNumber(arg, out double v)) nums.Add(v);
            else if (arg is DirectTextLiteralValue d && TryDirectTextNumber(d, out double dv)) nums.Add(dv);
        }

        double? k = null;
        if (needsK)
        {
            if (args[kIndex] is ErrorValue ek) return ek;
            var kc = ToNumber(args[kIndex]);
            if (!double.IsFinite(kc)) return ErrorValue.Num;
            k = kc;
        }

        switch (funcNum)
        {
            case 1:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Average());
            case 2:  return new NumberValue(nums.Count);
            case 3:
            {
                int countA = 0;
                for (int i = 2; i < args.Count; i++)
                {
                    if (i == kIndex) continue;
                    var arg = args[i];
                    if (arg is ErrorValue err)
                    {
                        if (ignoreErrors) continue;
                        return err;
                    }
                    if (arg is RangeValue rv)
                    {
                        foreach (var cell in rv.Flatten())
                        {
                            if (cell is ErrorValue ce)
                            {
                                if (ignoreErrors) continue;
                                return ce;
                            }
                            if (cell is not BlankValue) countA++;
                        }
                    }
                    else if (arg is not BlankValue) countA++;
                }
                return new NumberValue(countA);
            }
            case 4:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Max());
            case 5:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Min());
            case 6:  return NumberResult(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (a, x) => a * x));
            case 7:
            {
                if (nums.Count < 2) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(Math.Sqrt(nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1)));
            }
            case 8:
            {
                if (nums.Count == 0) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(Math.Sqrt(nums.Sum(x => (x - mean) * (x - mean)) / nums.Count));
            }
            case 9:  return NumberResult(nums.Sum());
            case 10:
            {
                if (nums.Count < 2) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1));
            }
            case 11:
            {
                if (nums.Count == 0) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(nums.Sum(x => (x - mean) * (x - mean)) / nums.Count);
            }
            case 12:
            {
                if (nums.Count == 0) return ErrorValue.Num;
                var s = nums.OrderBy(x => x).ToList();
                int n = s.Count;
                return NumberResult(n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0);
            }
            case 13: // MODE.SNGL
            {
                if (nums.Count == 0) return ErrorValue.NA;
                var counts = nums.GroupBy(x => x).Select(g => (g.Key, Count: g.Count())).ToList();
                int maxCount = counts.Max(c => c.Count);
                if (maxCount < 2) return ErrorValue.NA;
                return NumberResult(counts.First(c => c.Count == maxCount).Key);
            }
            case 14: // LARGE
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int ki = (int)Math.Truncate(k!.Value);
                if (ki < 1 || ki > nums.Count) return ErrorValue.Num;
                var s = nums.OrderByDescending(x => x).ToList();
                return NumberResult(s[ki - 1]);
            }
            case 15: // SMALL
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int ki = (int)Math.Truncate(k!.Value);
                if (ki < 1 || ki > nums.Count) return ErrorValue.Num;
                var s = nums.OrderBy(x => x).ToList();
                return NumberResult(s[ki - 1]);
            }
            case 16: // PERCENTILE.INC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                if (k!.Value < 0 || k.Value > 1) return ErrorValue.Num;
                return NumberResult(PercentileIncCalc(nums, k.Value));
            }
            case 17: // QUARTILE.INC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int q = (int)Math.Truncate(k!.Value);
                if (q < 0 || q > 4) return ErrorValue.Num;
                return NumberResult(PercentileIncCalc(nums, q / 4.0));
            }
            case 18: // PERCENTILE.EXC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                if (k!.Value <= 0 || k.Value >= 1) return ErrorValue.Num;
                return NumberResult(PercentileExcCalc(nums, k.Value));
            }
            case 19: // QUARTILE.EXC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int q = (int)Math.Truncate(k!.Value);
                if (q < 1 || q > 3) return ErrorValue.Num;
                return NumberResult(PercentileExcCalc(nums, q / 4.0));
            }
            default:
                return ErrorValue.Value;
        }
    }

    private static double PercentileIncCalc(List<double> nums, double p)
    {
        var s = nums.OrderBy(x => x).ToList();
        int n = s.Count;
        if (n == 1) return s[0];
        double pos = p * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return s[lo];
        return s[lo] + (pos - lo) * (s[hi] - s[lo]);
    }

    private static double PercentileExcCalc(List<double> nums, double p)
    {
        var s = nums.OrderBy(x => x).ToList();
        int n = s.Count;
        double pos = p * (n + 1) - 1;
        if (pos < 0 || pos > n - 1) throw new FormulaEvalException("#NUM!", "k out of range");
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return s[lo];
        return s[lo] + (pos - lo) * (s[hi] - s[lo]);
    }
}
