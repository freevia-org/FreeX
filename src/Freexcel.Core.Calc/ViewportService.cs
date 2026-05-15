using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Implementation of IViewportService that prepares data for the UI.
/// Handles coordinate mapping, sparse data retrieval, and conditional formatting.
/// </summary>
public sealed class ViewportService : IViewportService
{
    public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null)
        {
            return new ViewportModel([], [], [], null, []);
        }

        var cells = new List<DisplayCell>();
        var rowMetrics = new List<RowMetric>();
        var colMetrics = new List<ColMetric>();

        // Calculate Row Metrics — iterate until we've filled the available height, skipping hidden rows
        const uint MaxRow = CellAddress.MaxRow;
        double topOffset = 0;
        for (uint r = request.TopRow; r <= MaxRow; r++)
        {
            if (IsRowHidden(sheet, r)) continue;
            double height = sheet.RowHeights.GetValueOrDefault(r, sheet.DefaultRowHeight);
            rowMetrics.Add(new RowMetric(r, height, topOffset));
            topOffset += height;
            if (topOffset > request.AvailableHeight) break;
        }

        // Calculate Column Metrics — iterate until we've filled the available width
        const uint MaxCol = CellAddress.MaxCol;
        double leftOffset = 0;
        for (uint c = request.LeftCol; c <= MaxCol; c++)
        {
            if (sheet.IsColEffectivelyHidden(c)) continue;
            double width = sheet.ColumnWidths.GetValueOrDefault(c, sheet.DefaultColumnWidth) * 8;
            colMetrics.Add(new ColMetric(c, width, leftOffset));
            leftOffset += width;
            if (leftOffset > request.AvailableWidth) break;
        }

        // Retrieve Cells in Viewport
        foreach (var rowMetric in rowMetrics)
        {
            foreach (var colMetric in colMetrics)
            {
                var cell = sheet.GetCell(rowMetric.Row, colMetric.Col);
                if (cell != null)
                {
                    var style = workbook.GetStyle(cell.StyleId);

                    // Evaluate conditional formats and merge any triggered CF style on top
                    var addr = new CellAddress(sheetId, rowMetric.Row, colMetric.Col);
                    var cfStyle = EvaluateConditionalFormats(sheet, addr, cell.Value, workbook);
                    if (cfStyle != null)
                        style = MergeStyles(style, cfStyle);

                    cells.Add(new DisplayCell(
                        rowMetric.Row, colMetric.Col,
                        cell.Value,
                        NumberFormatter.Format(cell.Value, style.NumberFormat),
                        request.IncludeFormulas ? cell.FormulaText : null,
                        cell.StyleId,
                        null,
                        style
                    ));
                }
            }
        }

        var frozenPanes = (sheet.FrozenRows > 0 || sheet.FrozenCols > 0)
            ? new FrozenPaneState(sheet.FrozenRows, sheet.FrozenCols)
            : null;

        return new ViewportModel(cells, rowMetrics, colMetrics, frozenPanes, []);
    }

    public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return null;
        if (zoom <= 0) return null;

        // Apply zoom to incoming coordinates
        double targetX = x / zoom;
        double targetY = y / zoom;
        if (targetX < 0 || targetY < 0) return null;

        var row = HitTestRow(sheet, targetY);
        var col = HitTestColumn(sheet, targetX);
        if (row is null || col is null) return null;

        return new CellAddress(sheetId, row.Value, col.Value);
    }

    private static uint? HitTestRow(Sheet sheet, double y)
    {
        double top = 0;
        for (uint row = 1; row <= CellAddress.MaxRow; row++)
        {
            if (IsRowHidden(sheet, row)) continue;

            var height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
            if (y < top + height)
                return row;

            top += height;
        }

        return null;
    }

    private static uint? HitTestColumn(Sheet sheet, double x)
    {
        double left = 0;
        for (uint col = 1; col <= CellAddress.MaxCol; col++)
        {
            if (sheet.IsColEffectivelyHidden(col)) continue;

            var width = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
            if (x < left + width)
                return col;

            left += width;
        }

        return null;
    }

    private static bool IsRowHidden(Sheet sheet, uint row) =>
        sheet.IsRowEffectivelyHidden(row);

    // ── Conditional format evaluation ─────────────────────────────────────────

    /// <summary>
    /// Evaluates all conditional format rules that cover <paramref name="addr"/> (ordered by
    /// Priority ascending = highest precedence first). Returns the first matching rule's style,
    /// or null when no rule fires.
    /// </summary>
    private static readonly FormulaEvaluator _cfEvaluator = new();

    private static CellStyle? EvaluateConditionalFormats(
        Sheet sheet, CellAddress addr, ScalarValue value, Workbook workbook)
    {
        if (sheet.ConditionalFormats.Count == 0)
            return null;

        var rules = sheet.ConditionalFormats
            .Where(cf => cf.AppliesTo.Contains(addr))
            .OrderBy(cf => cf.Priority);

        foreach (var cf in rules)
        {
            // ColorScale and DataBar always apply when in range — return immediately.
            if (cf.RuleType == CfRuleType.ColorScale)
                return ComputeColorScaleStyle(cf, sheet, addr, value);
            if (cf.RuleType == CfRuleType.DataBar)
                return new CellStyle { FillColor = cf.DataBarColor.ToCellColor() };

            bool conditionMet = cf.RuleType switch
            {
                CfRuleType.CellValue    => MatchesCellValue(cf, value),
                CfRuleType.AboveAverage => MatchesAboveAverage(cf, sheet, addr, value),
                CfRuleType.Formula      => MatchesFormula(cf, sheet, addr, workbook),
                _                       => false
            };

            if (conditionMet)
                return cf.FormatIfTrue; // may be null if rule has no visible format

            // StopIfTrue stops further evaluation only when the condition is true.
            // If condition was false, continue regardless of StopIfTrue.
        }

        return null;
    }

    // ── Formula CF evaluation ─────────────────────────────────────────────────

    private static bool MatchesFormula(ConditionalFormat cf, Sheet sheet, CellAddress addr, Workbook workbook)
    {
        if (string.IsNullOrWhiteSpace(cf.FormulaText)) return false;
        try
        {
            // Shift relative references from the CF range's top-left to the current cell.
            int dr = (int)addr.Row - (int)cf.AppliesTo.Start.Row;
            int dc = (int)addr.Col - (int)cf.AppliesTo.Start.Col;
            var formulaText = dr == 0 && dc == 0
                ? cf.FormulaText
                : ShiftCfFormula(cf.FormulaText, dr, dc);

            var result = _cfEvaluator.Evaluate("=" + formulaText, sheet, workbook);
            return result switch
            {
                BoolValue bv   => bv.Value,
                NumberValue nv => nv.Value != 0,
                _              => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static string ShiftCfFormula(string formulaText, int dr, int dc)
    {
        try
        {
            var ast = new Parser(new Lexer("=" + formulaText).Tokenize()).Parse();
            var shifted = ShiftAst(ast, dr, dc);
            return FormulaSerializer.Serialize(shifted);
        }
        catch
        {
            return formulaText;
        }
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr   => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr  => rr with
            {
                Start = ShiftCellRef(rr.Start, dr, dc),
                End   = ShiftCellRef(rr.End,   dr, dc)
            },
            BinaryOpNode bin => bin with
            {
                Left  = ShiftAst(bin.Left,  dr, dc),
                Right = ShiftAst(bin.Right, dr, dc)
            },
            UnaryOpNode un   => un with { Operand = ShiftAst(un.Operand, dr, dc) },
            FunctionCallNode fn => fn with
            {
                Arguments = fn.Arguments.Select(a => ShiftAst(a, dr, dc)).ToList()
            },
            _ => node
        };
    }

    private static CellRefNode ShiftCellRef(CellRefNode cr, int dr, int dc)
    {
        uint newRow = cr.IsRowAbsolute ? cr.Row
            : (uint)Math.Max(1, (int)cr.Row + dr);
        uint newColNum = cr.IsColAbsolute ? cr.ColumnNumber
            : (uint)Math.Max(1, (int)cr.ColumnNumber + dc);
        var newColName = cr.IsColAbsolute ? cr.ColumnName
            : CellAddress.NumberToColumnName(newColNum);
        return cr with { Row = newRow, ColumnName = newColName };
    }

    // ── CellValue matching ────────────────────────────────────────────────────

    private static bool MatchesCellValue(ConditionalFormat cf, ScalarValue value)
    {
        // Attempt numeric comparison first, fall back to string
        if (TryGetDouble(value, out double d))
        {
            if (!TryParseDouble(cf.Value1, out double v1)) return false;

            return cf.Operator switch
            {
                CfOperator.Equal              => d == v1,
                CfOperator.NotEqual           => d != v1,
                CfOperator.GreaterThan        => d > v1,
                CfOperator.GreaterThanOrEqual => d >= v1,
                CfOperator.LessThan           => d < v1,
                CfOperator.LessThanOrEqual    => d <= v1,
                CfOperator.Between            => TryParseDouble(cf.Value2, out double v2) && d >= v1 && d <= v2,
                CfOperator.NotBetween         => TryParseDouble(cf.Value2, out double v2b) && !(d >= v1 && d <= v2b),
                _                             => false
            };
        }
        else
        {
            // String fallback — only Equal / NotEqual make sense
            var s = GetString(value);
            return cf.Operator switch
            {
                CfOperator.Equal    => string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
                CfOperator.NotEqual => !string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
                _                  => false
            };
        }
    }

    // ── AboveAverage matching ─────────────────────────────────────────────────

    private static bool MatchesAboveAverage(
        ConditionalFormat cf, Sheet sheet, CellAddress addr, ScalarValue value)
    {
        if (!TryGetDouble(value, out double cellVal)) return false;

        // Collect all numeric values in the CF range
        double sum = 0;
        int count = 0;
        foreach (var a in cf.AppliesTo.AllCells())
        {
            var v = sheet.GetValue(a);
            if (TryGetDouble(v, out double x)) { sum += x; count++; }
        }
        if (count == 0) return false;
        double avg = sum / count;

        return cf.AboveAverage ? cellVal > avg : cellVal < avg;
    }

    // ── ColorScale ────────────────────────────────────────────────────────────

    private static CellStyle? ComputeColorScaleStyle(
        ConditionalFormat cf, Sheet sheet, CellAddress addr, ScalarValue value)
    {
        if (!TryGetDouble(value, out double cellVal)) return null;

        // Gather all numeric values in the range
        var nums = cf.AppliesTo.AllCells()
            .Select(a => sheet.GetValue(a))
            .Where(v => TryGetDouble(v, out _))
            .Select(v => { TryGetDouble(v, out double x); return x; })
            .ToList();

        if (nums.Count == 0) return null;

        double min = nums.Min();
        double max = nums.Max();
        if (max == min) return new CellStyle { FillColor = cf.MinColor.ToCellColor() };

        double t = (cellVal - min) / (max - min); // 0..1

        CellColor interpolated;
        if (cf.UseThreeColorScale)
        {
            // Two-segment interpolation through MidColor at t = 0.5
            interpolated = t <= 0.5
                ? Lerp(cf.MinColor, cf.MidColor, t * 2)
                : Lerp(cf.MidColor, cf.MaxColor, (t - 0.5) * 2);
        }
        else
        {
            interpolated = Lerp(cf.MinColor, cf.MaxColor, t);
        }

        return new CellStyle { FillColor = interpolated };
    }

    private static CellColor Lerp(RgbColor a, RgbColor b, double t)
    {
        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new CellColor(r, g, bl);
    }

    // ── Style merging ─────────────────────────────────────────────────────────

    /// <summary>
    /// Merges a CF override style on top of a base style.
    /// CF properties override base only when they represent an actual override
    /// (non-null fill, non-default font properties set by the CF author).
    /// </summary>
    private static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle)
    {
        var result = (baseStyle ?? CellStyle.Default).Clone();

        if (cfStyle.FillColor.HasValue)
            result.FillColor = cfStyle.FillColor;

        // For font properties we treat any non-default CF value as an explicit override
        if (cfStyle.Bold)
            result.Bold = true;
        if (cfStyle.Italic)
            result.Italic = true;
        if (cfStyle.Underline)
            result.Underline = true;
        if (cfStyle.FontColor != CellColor.Black)
            result.FontColor = cfStyle.FontColor;

        return result;
    }

    // ── Value helpers ─────────────────────────────────────────────────────────

    private static bool TryGetDouble(ScalarValue value, out double result)
    {
        if (value is NumberValue nv) { result = nv.Value; return true; }
        result = 0;
        return false;
    }

    private static bool TryParseDouble(string? text, out double result)
    {
        if (text is null) { result = 0; return false; }
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static string GetString(ScalarValue value) => value switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        _ => ""
    };
}
