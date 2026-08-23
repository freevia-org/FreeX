using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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

        // Resolve reference: use args[1] when present; otherwise use the formula cell.
        // We don't have access to the original AST node here, so we read the
        // computed scalar/range (built by the evaluator's standard arg expansion).
        uint row = ctx.CurrentCellAddress?.Row ?? 1;
        uint col = ctx.CurrentCellAddress?.Col ?? 1;
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
                if (rv.SheetName is not null)
                    sheet = ctx.CurrentWorkbook?.GetSheet(rv.SheetName);
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
            {
                var address = $"${CellAddress.NumberToColumnName(col)}${row}";
                if (sheet is not null && sheet != ctx.CurrentSheet)
                    address = $"{SheetNameFormatter.QuoteIfNeeded(sheet.Name)}!{address}";
                return new TextValue(address);
            }
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
                bool locked = style?.Locked ?? true;
                return new NumberValue(locked ? 1 : 0);
            }
            case "width":
            {
                if (sheet is null) return new NumberValue(8);
                // Excel reports 0 for a hidden or outline-collapsed column's width, not the
                // width it would display if shown (IsColEffectivelyHidden ORs both mechanisms).
                if (sheet.IsColEffectivelyHidden(col)) return new NumberValue(0);
                var width = sheet.ColumnWidths.TryGetValue(col, out var w)
                    ? w
                    : sheet.DefaultColumnWidth;
                return new NumberValue(Math.Round(width, 0, MidpointRounding.AwayFromZero));
            }
            case "filename":
                return new TextValue(CellFilenameInfo(ctx, sheet));
            case "format":
                return new TextValue(CellFormatInfo(style?.NumberFormat));
            case "color":
                return new NumberValue(CellNegativeSectionUsesColor(style?.NumberFormat) ? 1 : 0);
            case "parentheses":
                return new NumberValue(CellPositiveOrAllSectionUsesParentheses(style?.NumberFormat) ? 1 : 0);
            case "prefix":
                return new TextValue(CellPrefixCode(style, cellValue));
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

    private static string CellPrefixCode(CellStyle? style, ScalarValue cellValue) =>
        (style?.HorizontalAlignment ?? HorizontalAlignment.General) switch
        {
            // The "label prefix" is a Lotus-1-2-3-era concept that only ever applies to TEXT
            // labels: Excel's own CELL("prefix") returns "" for a number/blank/logical/error cell
            // regardless of its alignment (e.g. an explicitly left-aligned numeric ID column still
            // reports ""). Gate every explicit-alignment branch on cellValue being TextValue, same
            // as the General-alignment branch below already does (added by the R33 fix).
            HorizontalAlignment.Left => cellValue is TextValue ? "'" : "",
            HorizontalAlignment.Center => cellValue is TextValue ? "^" : "",
            HorizontalAlignment.Right => cellValue is TextValue ? "\"" : "",
            // Fill repeats the cell text to fill the column; Excel reports the
            // fill-alignment label prefix as a single backslash.
            HorizontalAlignment.Fill => cellValue is TextValue ? "\\" : "",
            // General left-justifies TEXT (Excel reports the apostrophe label prefix
            // for it, same as an explicit Left alignment) but right-justifies/has no
            // label for numbers and blanks.
            HorizontalAlignment.General => cellValue is TextValue ? "'" : "",
            _ => ""
        };

    private static string CellFormatInfo(string? numberFormat)
    {
        var code = CellFormatCode(numberFormat);
        if (CellNegativeSectionUsesColor(numberFormat))
            code += "-";
        if (CellPositiveOrAllSectionUsesParentheses(numberFormat))
            code += "()";
        return code;
    }

    private static string CellFormatCode(string? numberFormat)
    {
        var normalized = NormalizeCellNumberFormat(numberFormat);
        if (normalized.Length == 0 || normalized == "general")
            return "G";

        if (normalized.Length >= 2 && normalized[0] == '(' && normalized[^1] == ')')
            normalized = normalized[1..^1];

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
            "h:mm" => "D9",
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
        // '_' and '*' are padding-escapes: like '\', they consume the immediately
        // following character as a non-literal argument (a spacer width / fill char),
        // not just themselves -- so that char must be skipped too, or it leaks into
        // the normalized format and breaks the exact-match lookup below.
        bool skipNext = false;

        foreach (var ch in numberFormat)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

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

            if (bracketed)
                continue;

            if (ch is '_' or '*')
            {
                skipNext = true;
                continue;
            }

            if (ch == ' ')
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
            // A bracketed token beginning with '$' is always an OOXML locale/currency
            // tag (e.g. "$-409", "$$-409", "$£-809", "$€-407"), never a color spec --
            // Excel's color tokens are named colors ([Red], [Color10], ...) and only
            // ever start with a letter.
            if (token[0] == '$') continue;
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
        return negativeSection is not null && CellFormatSectionUsesParentheses(negativeSection);
    }

    private static bool CellPositiveOrAllSectionUsesParentheses(string? numberFormat)
    {
        var sections = SplitCellFormatSections(numberFormat);
        return sections.Count > 0 && CellFormatSectionUsesParentheses(sections[0]);
    }

    private static bool CellFormatSectionUsesParentheses(string section)
    {
        bool quoted = false;
        bool escaped = false;
        bool bracketed = false;
        bool hasOpen = false;
        bool hasClose = false;

        foreach (var ch in section)
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
                try { return new TextValue(EnsureTrailingDirectorySeparator(Environment.CurrentDirectory)); }
                catch { return new TextValue(""); }
            case "numfile":
                return new NumberValue(ctx.CurrentWorkbook?.SheetCount ?? 1);
            case "origin":
                return new TextValue("$A:$A$1");
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
                return ErrorValue.NA;
            default:
                return ErrorValue.Value;
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        string.IsNullOrEmpty(path) || System.IO.Path.EndsInDirectorySeparator(path)
            ? path
            : path + System.IO.Path.DirectorySeparatorChar;

    // CELL("filename") reproduces Excel's "drive:\path\[filename]sheetname" result once the
    // workbook has an on-disk path (Workbook.FilePath, set by the host app's open/save code);
    // a never-saved in-memory-only workbook has no path and Excel returns "".
    private static string CellFilenameInfo(IEvalContext ctx, Sheet? sheet)
    {
        var filePath = ctx.CurrentWorkbook?.FilePath;
        if (string.IsNullOrEmpty(filePath)) return "";

        var directory = System.IO.Path.GetDirectoryName(filePath);
        var fileName = System.IO.Path.GetFileName(filePath);
        var sheetName = (sheet ?? ctx.CurrentSheet)?.Name ?? "";
        var directoryWithSeparator = string.IsNullOrEmpty(directory)
            ? ""
            : EnsureTrailingDirectorySeparator(directory);

        return $"{directoryWithSeparator}[{fileName}]{sheetName}";
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
        if (!TryCoerceIsEvenOddNumber(value, out long n, out var error)) return error;
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
        if (!TryCoerceIsEvenOddNumber(value, out long n, out var error)) return error;
        return new BoolValue(n % 2 != 0);
    }

    private static bool TryCoerceIsEvenOddNumber(ScalarValue value, out long number, out ScalarValue error)
    {
        number = 0;
        error = ErrorValue.Value;

        double numeric;
        try
        {
            numeric = ToNumber(value);
        }
        catch (FormulaEvalException)
        {
            return false;
        }

        if (!TryTruncateToLong(numeric, out number))
        {
            error = ErrorValue.Num;
            return false;
        }

        return true;
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

        int kIndex = needsK ? args.Count - 1 : -1;

        if (funcNum == 3)
            return AggregateCountA(args, ctx, kIndex, ignoreErrors, ignoreHiddenRows, ignoreNestedAggregates);
        if (funcNum is >= 1 and <= 11)
            return AggregateNumericStreaming(args, ctx, funcNum, kIndex, ignoreErrors, ignoreHiddenRows, ignoreNestedAggregates);

        double? k = null;
        if (needsK)
        {
            if (args[kIndex] is ErrorValue ek) return ek;
            var kc = ToNumber(args[kIndex]);
            if (!double.IsFinite(kc)) return ErrorValue.Num;
            k = kc;
        }

        if (funcNum == 13)
            return AggregateModeSnglStreaming(args, ctx, ignoreErrors, ignoreHiddenRows, ignoreNestedAggregates);

        var nums = new List<double>();
        var collectError = CollectAggregateNumbers(args, ctx, kIndex, ignoreErrors, ignoreHiddenRows, ignoreNestedAggregates, nums);
        if (collectError is not null) return collectError;

        switch (funcNum)
        {
            case 12:
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int n = nums.Count;
                int mid = n / 2;
                if (n % 2 == 1) return NumberResult(SelectKthSmallest(nums, mid));

                double lower = SelectKthSmallest(nums, mid - 1);
                double upper = SelectKthSmallest(nums, mid);
                return NumberResult((lower + upper) / 2.0);
            }
            case 14: // LARGE
            {
                if (nums.Count == 0) return ErrorValue.Num;
                double kd = Math.Truncate(k!.Value);
                if (kd < 1 || kd > nums.Count) return ErrorValue.Num;
                return NumberResult(SelectKthSmallest(nums, nums.Count - (int)kd));
            }
            case 15: // SMALL
            {
                if (nums.Count == 0) return ErrorValue.Num;
                double kd = Math.Truncate(k!.Value);
                if (kd < 1 || kd > nums.Count) return ErrorValue.Num;
                return NumberResult(SelectKthSmallest(nums, (int)kd - 1));
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

    private static ErrorValue? CollectAggregateNumbers(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        int kIndex,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates,
        List<double> nums)
    {
        // Processes one genuine worksheet-range argument (or one area of a union argument -- see
        // the UnionValue branch below) into `nums`, honoring ignore-errors/hidden-row/nested-
        // AGGREGATE options. Extracted to a local function so a UnionValue's areas can each run
        // through the identical logic a plain RangeValue argument does, mirroring
        // BuiltInFunctions.Subtotal.cs's ProcessSubtotalRange pattern (R112-aggregate-union-ref1).
        ErrorValue? CollectRange(RangeValue rv)
        {
            // See BuiltInFunctions.Subtotal.cs's isReference guard (R19-formula-functions-edge-1,
            // R25-aggregate-subtotal-deep-3): only a genuine worksheet reference carries
            // coordinates that map to real cells, so gate hidden-row / nested SUBTOTAL-AGGREGATE
            // exclusion on the explicit RangeValue.IsSheetReference provenance flag. A computed/
            // virtual array (FILTER, SORT, ...) defaults StartRow/StartCol to (1,1) with SheetName
            // null — field-for-field identical to a genuine same-sheet A1-anchored reference — so
            // no coordinate heuristic can distinguish the two without wrongly dropping elements.
            bool isReference = rv.IsSheetReference;
            for (int r = 0; r < rv.RowCount; r++)
            {
                uint absRow = rv.StartRow + (uint)r;
                if (ignoreHiddenRows && isReference && IsAggregateRowHidden(ctx, rv, absRow)) continue;
                for (int c = 0; c < rv.ColCount; c++)
                {
                    uint absCol = rv.StartCol + (uint)c;
                    if (ignoreNestedAggregates && isReference && IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                    var cell = rv.Cells[r, c];
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (TryCellNumber(cell, out double value)) nums.Add(value);
                }
            }
            return null;
        }

        // Collect from positional value args (skip funcNum, options, and a potential k arg).
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
                var rangeError = CollectRange(rv);
                if (rangeError is not null) return rangeError;
            }
            else if (arg is UnionValue uv)
            {
                // R112-aggregate-union-ref1: a parenthesized union argument (e.g.
                // AGGREGATE(9,0,(A1:A5,C1:C5))) is a genuine single reference, exactly as it is for
                // SUBTOTAL/SUM/etc. (see BuiltInFunctions.Subtotal.cs's own UnionValue branch). Each
                // area inside the union is a real RangeValue carrying its own IsSheetReference/
                // StartRow/SheetName, so process each area individually through the same CollectRange
                // logic rather than treating the opaque UnionValue as a scalar (which silently
                // contributed nothing before this fix).
                foreach (var area in uv.Areas)
                {
                    var areaError = CollectRange(area);
                    if (areaError is not null) return areaError;
                }
            }
            else if (TryCellNumber(arg, out double value)) nums.Add(value);
            else if (arg is DirectTextLiteralValue direct && TryDirectTextNumber(direct, out double directValue))
                nums.Add(directValue);
        }

        return null;
    }

    private static ScalarValue AggregateModeSnglStreaming(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates)
    {
        var mode = new AggregateModeAccumulator();

        // See CollectAggregateNumbers's CollectRange local function for why this is extracted
        // (R112-aggregate-union-ref1): a UnionValue's areas must run through the identical
        // per-range logic a plain RangeValue argument does.
        ErrorValue? CollectRange(RangeValue rv)
        {
            // See BuiltInFunctions.Subtotal.cs's isReference guard (R19-formula-functions-edge-1,
            // R25-aggregate-subtotal-deep-3): only a genuine worksheet reference carries
            // coordinates that map to real cells, so gate hidden-row / nested SUBTOTAL-AGGREGATE
            // exclusion on the explicit RangeValue.IsSheetReference provenance flag. A computed/
            // virtual array (FILTER, SORT, ...) defaults StartRow/StartCol to (1,1) with SheetName
            // null — field-for-field identical to a genuine same-sheet A1-anchored reference — so
            // no coordinate heuristic can distinguish the two without wrongly dropping elements.
            bool isReference = rv.IsSheetReference;
            for (int r = 0; r < rv.RowCount; r++)
            {
                uint absRow = rv.StartRow + (uint)r;
                if (ignoreHiddenRows && isReference && IsAggregateRowHidden(ctx, rv, absRow)) continue;
                for (int c = 0; c < rv.ColCount; c++)
                {
                    uint absCol = rv.StartCol + (uint)c;
                    if (ignoreNestedAggregates && isReference && IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                    var cell = rv.Cells[r, c];
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (TryCellNumber(cell, out double value)) mode.Add(value);
                }
            }
            return null;
        }

        for (int i = 2; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is ErrorValue err)
            {
                if (ignoreErrors) continue;
                return err;
            }

            if (arg is RangeValue rv)
            {
                var rangeError = CollectRange(rv);
                if (rangeError is not null) return rangeError;
            }
            else if (arg is UnionValue uv)
            {
                // R112-aggregate-union-ref1: see CollectAggregateNumbers's identical UnionValue
                // branch -- a parenthesized union argument is a genuine single reference and each
                // area must be processed individually.
                foreach (var area in uv.Areas)
                {
                    var areaError = CollectRange(area);
                    if (areaError is not null) return areaError;
                }
            }
            else if (TryCellNumber(arg, out double value))
            {
                mode.Add(value);
            }
            else if (arg is DirectTextLiteralValue direct && TryDirectTextNumber(direct, out double directValue))
            {
                mode.Add(directValue);
            }
        }

        return mode.TryGetValue(out var result)
            ? NumberResult(result)
            : ErrorValue.NA;
    }

    private static ScalarValue AggregateCountA(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        int kIndex,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates)
    {
        long count = 0;

        // See CollectAggregateNumbers's CollectRange local function for why this is extracted
        // (R112-aggregate-union-ref1): a UnionValue's areas must run through the identical
        // per-range logic a plain RangeValue argument does.
        ErrorValue? CollectRange(RangeValue rv)
        {
            // See BuiltInFunctions.Subtotal.cs's isReference guard (R19-formula-functions-edge-1,
            // R25-aggregate-subtotal-deep-3): only a genuine worksheet reference carries
            // coordinates that map to real cells, so gate hidden-row / nested SUBTOTAL-AGGREGATE
            // exclusion on the explicit RangeValue.IsSheetReference provenance flag. A computed/
            // virtual array (FILTER, SORT, ...) defaults StartRow/StartCol to (1,1) with SheetName
            // null — field-for-field identical to a genuine same-sheet A1-anchored reference — so
            // no coordinate heuristic can distinguish the two without wrongly dropping elements.
            bool isReference = rv.IsSheetReference;
            for (int r = 0; r < rv.RowCount; r++)
            {
                uint absRow = rv.StartRow + (uint)r;
                if (ignoreHiddenRows && isReference && IsAggregateRowHidden(ctx, rv, absRow)) continue;
                for (int c = 0; c < rv.ColCount; c++)
                {
                    uint absCol = rv.StartCol + (uint)c;
                    if (ignoreNestedAggregates && isReference && IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                    var cell = rv.Cells[r, c];
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (cell is not BlankValue) count++;
                }
            }
            return null;
        }

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
                var rangeError = CollectRange(rv);
                if (rangeError is not null) return rangeError;
            }
            else if (arg is UnionValue uv)
            {
                // R112-aggregate-union-ref1: see CollectAggregateNumbers's identical UnionValue
                // branch -- a parenthesized union argument is a genuine single reference (e.g.
                // AGGREGATE(3,0,(A1:A5,C1:C5)) for COUNTA-mode) and each area must be counted
                // individually rather than the whole union matching "not BlankValue" and
                // incrementing count by exactly 1.
                foreach (var area in uv.Areas)
                {
                    var areaError = CollectRange(area);
                    if (areaError is not null) return areaError;
                }
            }
            else if (arg is not BlankValue)
            {
                count++;
            }
        }

        return new NumberValue(count);
    }

    private static ScalarValue AggregateNumericStreaming(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        int funcNum,
        int kIndex,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates)
    {
        var numeric = new NumericAggregateAccumulator();

        // See CollectAggregateNumbers's CollectRange local function for why this is extracted
        // (R112-aggregate-union-ref1): a UnionValue's areas must run through the identical
        // per-range logic a plain RangeValue argument does.
        ErrorValue? CollectRange(RangeValue rv)
        {
            // See BuiltInFunctions.Subtotal.cs's isReference guard (R19-formula-functions-edge-1,
            // R25-aggregate-subtotal-deep-3): only a genuine worksheet reference carries
            // coordinates that map to real cells, so gate hidden-row / nested SUBTOTAL-AGGREGATE
            // exclusion on the explicit RangeValue.IsSheetReference provenance flag. A computed/
            // virtual array (FILTER, SORT, ...) defaults StartRow/StartCol to (1,1) with SheetName
            // null — field-for-field identical to a genuine same-sheet A1-anchored reference — so
            // no coordinate heuristic can distinguish the two without wrongly dropping elements.
            bool isReference = rv.IsSheetReference;
            for (int r = 0; r < rv.RowCount; r++)
            {
                uint absRow = rv.StartRow + (uint)r;
                if (ignoreHiddenRows && isReference && IsAggregateRowHidden(ctx, rv, absRow)) continue;
                for (int c = 0; c < rv.ColCount; c++)
                {
                    uint absCol = rv.StartCol + (uint)c;
                    if (ignoreNestedAggregates && isReference && IsNestedSubtotalOrAggregateCell(ctx, rv, absRow, absCol)) continue;
                    var cell = rv.Cells[r, c];
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (TryCellNumber(cell, out double value)) numeric.Add(value, funcNum);
                }
            }
            return null;
        }

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
                var rangeError = CollectRange(rv);
                if (rangeError is not null) return rangeError;
            }
            else if (arg is UnionValue uv)
            {
                // R112-aggregate-union-ref1: see CollectAggregateNumbers's identical UnionValue
                // branch -- a parenthesized union argument is a genuine single reference and each
                // area must be processed individually.
                foreach (var area in uv.Areas)
                {
                    var areaError = CollectRange(area);
                    if (areaError is not null) return areaError;
                }
            }
            else if (TryCellNumber(arg, out double value))
            {
                numeric.Add(value, funcNum);
            }
            else if (arg is DirectTextLiteralValue direct && TryDirectTextNumber(direct, out double directValue))
            {
                numeric.Add(directValue, funcNum);
            }
        }

        return funcNum switch
        {
            1  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(numeric.Average),
            2  => new NumberValue(numeric.Count),
            // MAX/MIN return 0 for an all-non-numeric/empty range, matching the plain MAX()/MIN()
            // functions and real Excel (and BuiltInFunctions.Subtotal.cs's SUBTOTAL slow path) —
            // unlike AVERAGE/STDEV/VAR (1,7,8,10,11) which genuinely error (#DIV/0!) on an empty sample.
            4  => NumberResult(numeric.Count == 0 ? 0 : numeric.Max),
            5  => NumberResult(numeric.Count == 0 ? 0 : numeric.Min),
            6  => NumberResult(numeric.Count == 0 ? 0 : numeric.Product),
            7  => numeric.Count < 2 ? ErrorValue.DivByZero : NumberResult(Math.Sqrt(numeric.SampleVariance)),
            8  => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(Math.Sqrt(numeric.PopulationVariance)),
            9  => NumberResult(numeric.Sum),
            10 => numeric.Count < 2 ? ErrorValue.DivByZero : NumberResult(numeric.SampleVariance),
            11 => numeric.Count == 0 ? ErrorValue.DivByZero : NumberResult(numeric.PopulationVariance),
            _  => ErrorValue.Value
        };
    }

    private static bool IsAggregateRowHidden(IEvalContext ctx, RangeValue range, uint row)
    {
        return range.SheetName is null
            ? ctx.IsRowHidden(row)
            : ctx.IsRowHidden(range.SheetName, row);
    }

    private static double PercentileIncCalc(List<double> nums, double p)
    {
        int n = nums.Count;
        if (n == 1) return nums[0];
        double pos = p * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        double lower = SelectKthSmallest(nums, lo);
        if (lo == hi) return lower;
        double upper = SelectKthSmallest(nums, hi);
        return lower + (pos - lo) * (upper - lower);
    }

    private static double PercentileExcCalc(List<double> nums, double p)
    {
        int n = nums.Count;
        double pos = p * (n + 1) - 1;
        if (pos < 0 || pos > n - 1) throw new FormulaEvalException("#NUM!", "k out of range");
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        double lower = SelectKthSmallest(nums, lo);
        if (lo == hi) return lower;
        double upper = SelectKthSmallest(nums, hi);
        return lower + (pos - lo) * (upper - lower);
    }

    private sealed class AggregateModeAccumulator
    {
        private readonly Dictionary<double, AggregateModeCount> _counts = [];
        private int _ordinal;
        private int _bestCount;
        private int _bestOrdinal;
        private double _bestValue;

        public void Add(double value)
        {
            ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                _counts,
                value,
                out bool exists);
            if (exists)
                entry.Count++;
            else
                entry = new AggregateModeCount(1, _ordinal);

            if (entry.Count >= 2 &&
                (entry.Count > _bestCount ||
                 (entry.Count == _bestCount && entry.FirstOrdinal < _bestOrdinal)))
            {
                _bestCount = entry.Count;
                _bestOrdinal = entry.FirstOrdinal;
                _bestValue = value;
            }

            _ordinal++;
        }

        public bool TryGetValue(out double value)
        {
            value = _bestValue;
            return _bestCount >= 2;
        }
    }

    private struct AggregateModeCount
    {
        public int Count;
        public int FirstOrdinal;

        public AggregateModeCount(int count, int firstOrdinal)
        {
            Count = count;
            FirstOrdinal = firstOrdinal;
        }
    }

}
