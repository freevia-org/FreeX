using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A2 information and aggregate functions.

    // Defensive fallback if EvaluateAstAware routing is bypassed; the
    // FormulaEvaluator dispatches ISREF/ISFORMULA/FORMULATEXT/OFFSET/CELL to
    // AST-aware code paths before invoking this delegate.
    private static ScalarValue AstAwareStub(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => ErrorValue.Value;

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – CELL(info_type, [reference])
    // ════════════════════════════════════════════════════════════════════════

    internal static ScalarValue CellInfo(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
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
        var style = ResolveCellStyle(ctx, sheet, underlying, row, col);

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
                if (style is not null)
                    locked = style.Locked;
                return new NumberValue(sheet.IsProtected && locked ? 1 : 0);
            }
            case "width":
            {
                if (sheet is null) return new NumberValue(8);
                var width = sheet.ColumnWidths.TryGetValue(col, out var w)
                    ? w
                    : sheet.DefaultColumnWidth;
                return new NumberValue(Math.Round(width, 0, MidpointRounding.AwayFromZero));
            }
            case "filename":
                // In-memory workbook has no on-disk path; Excel compat is empty string.
                return new TextValue("");
            case "format":
                return new TextValue(CellFormatCode(style?.NumberFormat));
            case "color":
                return new NumberValue(CellNegativeSectionUsesColor(style?.NumberFormat) ? 1 : 0);
            case "parentheses":
                return new NumberValue(CellNegativeSectionUsesParentheses(style?.NumberFormat) ? 1 : 0);
            case "prefix":
                return new TextValue("");
            default:
                return ErrorValue.Value;
        }
    }

    private static CellStyle? ResolveCellStyle(IEvalContext ctx, Sheet? sheet, Cell? cell, uint row, uint col)
    {
        if (ctx.CurrentWorkbook is null || sheet is null) return null;
        if (cell is not null) return ctx.CurrentWorkbook.GetStyle(cell.StyleId);

        var styleOnly = sheet.GetStyleOnly(row, col);
        return styleOnly is null ? CellStyle.Default : ctx.CurrentWorkbook.GetStyle(styleOnly.Value);
    }

    private static string CellFormatCode(string? numberFormat)
    {
        var normalized = NormalizeCellNumberFormat(numberFormat);
        if (normalized.Length == 0 || normalized == "general")
            return "G";

        return normalized switch
        {
            "0" => "F0",
            "#,##0" => ",0",
            "0.00" => "F2",
            "#,##0.00" => ",2",
            "$#,##0" or "$#,##0;($#,##0)" => "C0",
            "$#,##0.00" or "$#,##0.00;($#,##0.00)" => "C2",
            "0%" => "P0",
            "0.00%" => "P2",
            "0.00e+00" or "0.00e+0" or "0e+00" or "0e+0" => "S2",
            "d-mmm-yy" or "dd-mmm-yy" => "D1",
            "d-mmm" or "dd-mmm" => "D2",
            "mmm-yy" => "D3",
            "m/d/yy" or "m/d/yyyy" or "mm/dd/yy" or "mm/dd/yyyy" or "m/d/yyh:mm" or "m/d/yyyyh:mm" => "D4",
            "mm/dd" or "m/d" => "D5",
            "h:mm:ssam/pm" => "D6",
            "h:mmam/pm" => "D7",
            "h:mm:ss" => "D8",
            _ => "G"
        };
    }

    private static string NormalizeCellNumberFormat(string? numberFormat)
    {
        if (string.IsNullOrWhiteSpace(numberFormat))
            return "";

        var chars = new List<char>(numberFormat.Length);
        bool quoted = false;
        bool escaped = false;
        bool bracketed = false;

        foreach (var ch in numberFormat)
        {
            if (ch == ';' && !quoted && !bracketed)
                break;

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
                continue;

            if (ch == '[')
            {
                bracketed = true;
                continue;
            }

            if (ch == ']')
            {
                bracketed = false;
                continue;
            }

            if (bracketed || ch is '_' or '*' or ' ')
                continue;

            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }

    private static bool CellNegativeSectionUsesColor(string? numberFormat)
    {
        var negativeSection = GetCellNegativeFormatSection(numberFormat);
        if (negativeSection is null) return false;

        foreach (var bracket in EnumerateBracketedFormatTokens(negativeSection))
        {
            var token = bracket.Trim();
            if (token.Length == 0) continue;
            if (token.StartsWith("$-", StringComparison.Ordinal)) continue;
            if (token[0] is '<' or '>' or '=') continue;
            if (token.Contains('=') || char.IsDigit(token[0])) continue;
            if (token.StartsWith("DBNum", StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }

        return false;
    }

    private static bool CellNegativeSectionUsesParentheses(string? numberFormat)
    {
        var negativeSection = GetCellNegativeFormatSection(numberFormat);
        if (negativeSection is null) return false;

        bool quoted = false;
        bool escaped = false;
        bool bracketed = false;
        bool hasOpen = false;
        bool hasClose = false;

        foreach (var ch in negativeSection)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
                continue;

            if (ch == '[')
            {
                bracketed = true;
                continue;
            }

            if (ch == ']')
            {
                bracketed = false;
                continue;
            }

            if (bracketed)
                continue;

            if (ch == '(') hasOpen = true;
            if (ch == ')') hasClose = true;
        }

        return hasOpen && hasClose;
    }

    private static string? GetCellNegativeFormatSection(string? numberFormat)
    {
        var sections = SplitCellFormatSections(numberFormat);
        return sections.Count >= 2 ? sections[1] : null;
    }

    private static List<string> SplitCellFormatSections(string? numberFormat)
    {
        var sections = new List<string>();
        if (string.IsNullOrEmpty(numberFormat))
            return sections;

        var current = new List<char>();
        bool quoted = false;
        bool escaped = false;

        foreach (var ch in numberFormat)
        {
            if (escaped)
            {
                current.Add(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                current.Add(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                current.Add(ch);
                quoted = !quoted;
                continue;
            }

            if (ch == ';' && !quoted)
            {
                sections.Add(new string(current.ToArray()));
                current.Clear();
                continue;
            }

            current.Add(ch);
        }

        sections.Add(new string(current.ToArray()));
        return sections;
    }

    private static IEnumerable<string> EnumerateBracketedFormatTokens(string section)
    {
        bool quoted = false;
        bool escaped = false;
        int tokenStart = -1;

        for (int i = 0; i < section.Length; i++)
        {
            var ch = section[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
                continue;

            if (ch == '[')
            {
                tokenStart = i + 1;
                continue;
            }

            if (ch == ']' && tokenStart >= 0)
            {
                yield return section[tokenStart..i];
                tokenStart = -1;
            }
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

    private static ScalarValue Isblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is BlankValue)
            : new BoolValue(args[0] is BlankValue);

    private static ScalarValue Isnumber(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is NumberValue or DateTimeValue)
            : new BoolValue(args[0] is NumberValue or DateTimeValue);

    private static ScalarValue Istext(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is TextValue)
            : new BoolValue(args[0] is TextValue);

    private static ScalarValue Iserror(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is ErrorValue)
            : new BoolValue(args[0] is ErrorValue);

    private static ScalarValue Iserr(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is ErrorValue error && error.Code != "#N/A")
            : new BoolValue(args[0] is ErrorValue error && error.Code != "#N/A");

    private static ScalarValue Isna(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is ErrorValue e2 && e2.Code == "#N/A")
            : new BoolValue(args[0] is ErrorValue e2 && e2.Code == "#N/A");

    private static ScalarValue Isnontext(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is not TextValue)
            : new BoolValue(args[0] is not TextValue);

    private static ScalarValue Islogical(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] is RangeValue range
            ? MapPredicateRange(range, value => value is BoolValue)
            : new BoolValue(args[0] is BoolValue);

    private static ScalarValue NFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, NScalar);
        return NScalar(args[0]);
    }

    private static ScalarValue NScalar(ScalarValue value) =>
        value switch
        {
            NumberValue nv   => nv,
            DateTimeValue dt => new NumberValue(dt.Value),
            BoolValue bv     => new NumberValue(bv.Value ? 1 : 0),
            ErrorValue ev    => ev,
            _                => new NumberValue(0)
        };

    private static ScalarValue Iseven(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, IsevenScalar);
        return IsevenScalar(args[0]);
    }

    private static ScalarValue IsevenScalar(ScalarValue value)
    {
        if (!TryTruncateToLong(ToNumber(value), out long n)) return ErrorValue.Num;
        return new BoolValue(n % 2 == 0);
    }

    private static ScalarValue Isodd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, IsoddScalar);
        return IsoddScalar(args[0]);
    }

    private static ScalarValue IsoddScalar(ScalarValue value)
    {
        if (!TryTruncateToLong(ToNumber(value), out long n)) return ErrorValue.Num;
        return new BoolValue(n % 2 != 0);
    }

    private static RangeValue MapPredicateRange(RangeValue range, Func<ScalarValue, bool> predicate)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                cells[r, c] = new BoolValue(predicate(range.Cells[r, c]));

        return new RangeValue(cells);
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
        bool ignoreHiddenRows = options == 1 || options == 3 || options == 5 || options == 7;
        bool ignoreNestedAggregates = options <= 3;

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
                foreach (var cell in AggregateVisibleCells(rv, ctx, ignoreHiddenRows, ignoreNestedAggregates))
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
                        foreach (var cell in AggregateVisibleCells(rv, ctx, ignoreHiddenRows, ignoreNestedAggregates))
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

    private static IEnumerable<ScalarValue> AggregateVisibleCells(
        RangeValue range,
        IEvalContext ctx,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates)
    {
        for (int r = 0; r < range.RowCount; r++)
        {
            uint absRow = range.StartRow + (uint)r;
            if (ignoreHiddenRows && IsAggregateRowHidden(ctx, range, absRow)) continue;
            for (int c = 0; c < range.ColCount; c++)
            {
                uint absCol = range.StartCol + (uint)c;
                if (ignoreNestedAggregates && IsNestedSubtotalOrAggregateCell(ctx, range, absRow, absCol)) continue;
                yield return range.Cells[r, c];
            }
        }
    }

    private static bool IsAggregateRowHidden(IEvalContext ctx, RangeValue range, uint row)
    {
        return range.SheetName is null
            ? ctx.IsRowHidden(row)
            : ctx.IsRowHidden(range.SheetName, row);
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
