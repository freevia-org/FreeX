using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxConditionalFormatClosedXmlMapper
{
    public static void Load(
        IXLWorksheet xlSheet,
        Sheet sheet,
        WorkbookTheme theme,
        Func<IXLStyle, WorkbookTheme, CellStyle> mapStyle,
        IReadOnlyList<int>? classicRulePriorities = null)
    {
        // Real per-rule priorities as read straight from the worksheet XML, in document order
        // (see XlsxFileAdapter.ReadAdvancedConditionalFormats). Using these instead of a private
        // 1..N counter keeps CellIs/Expression rules on the SAME priority sequence as the advanced
        // (ColorScale/DataBar/IconSet/long-tail) rules the caller already added to sheet.ConditionalFormats,
        // preserving the file's true relative evaluation order between the two rule families.
        var priorityQueue = classicRulePriorities is { Count: > 0 }
            ? new Queue<int>(classicRulePriorities)
            : null;
        int fallbackPriority = 1;
        int NextPriority() => priorityQueue is { Count: > 0 } ? priorityQueue.Dequeue() : fallbackPriority++;

        foreach (var xlCf in xlSheet.ConditionalFormats)
        {
            var sheetId = sheet.Id;

            // A rule's sqref can list multiple non-contiguous ranges (e.g. "A1:A5 C1:C5"); ClosedXML
            // exposes every one of them via IXLConditionalFormat.Ranges (xlCf.Range is only the first).
            // Read them all here -- the first becomes AppliesTo and the rest AdditionalRanges -- the
            // same way XlsxDataValidationClosedXmlMapper.Load handles multi-range data validations,
            // so a classic (CellIs/Expression) rule doesn't silently lose every range after the first
            // on load (P50).
            var xlRanges = xlCf.Ranges.Select(range => range.RangeAddress).ToArray();
            if (xlRanges.Length == 0)
            {
                NextPriority();
                continue;
            }

            var firstRange = xlRanges[0];
            var start = new CellAddress(sheetId,
                (uint)firstRange.FirstAddress.RowNumber,
                (uint)firstRange.FirstAddress.ColumnNumber);
            var end = new CellAddress(sheetId,
                (uint)firstRange.LastAddress.RowNumber,
                (uint)firstRange.LastAddress.ColumnNumber);
            var appliesTo = new GridRange(start, end);
            List<GridRange>? additionalRanges = null;
            if (xlRanges.Length > 1)
            {
                additionalRanges = new List<GridRange>(xlRanges.Length - 1);
                for (var i = 1; i < xlRanges.Length; i++)
                {
                    var range = xlRanges[i];
                    additionalRanges.Add(new GridRange(
                        new CellAddress(sheetId, (uint)range.FirstAddress.RowNumber, (uint)range.FirstAddress.ColumnNumber),
                        new CellAddress(sheetId, (uint)range.LastAddress.RowNumber, (uint)range.LastAddress.ColumnNumber)));
                }
            }

            if (xlCf.ConditionalFormatType == XLConditionalFormatType.CellIs)
            {
                var op = MapOperator(xlCf.Operator);
                if (op is null)
                {
                    NextPriority();
                    continue;
                }

                var values = xlCf.Values;
                string? v1 = values.TryGetValue(1, out var xv1) ? xv1.Value : null;
                string? v2 = values.TryGetValue(2, out var xv2) ? xv2.Value : null;

                var fmt = new ConditionalFormat
                {
                    AppliesTo = appliesTo,
                    AdditionalRanges = additionalRanges,
                    Priority = NextPriority(),
                    RuleType = CfRuleType.CellValue,
                    Operator = op.Value,
                    Value1 = v1,
                    Value2 = v2,
                    StopIfTrue = xlCf.StopIfTrue,
                    FormatIfTrue = mapStyle(xlCf.Style, theme)
                };
                sheet.ConditionalFormats.Add(fmt);
            }
            else if (xlCf.ConditionalFormatType == XLConditionalFormatType.Expression)
            {
                var values = xlCf.Values;
                string? formula = values.TryGetValue(1, out var xvf) ? xvf.Value : null;
                if (string.IsNullOrWhiteSpace(formula))
                {
                    NextPriority();
                    continue;
                }

                if (formula.StartsWith('='))
                    formula = formula[1..];

                var fmt = new ConditionalFormat
                {
                    AppliesTo = appliesTo,
                    AdditionalRanges = additionalRanges,
                    Priority = NextPriority(),
                    RuleType = CfRuleType.Formula,
                    FormulaText = formula,
                    StopIfTrue = xlCf.StopIfTrue,
                    FormatIfTrue = mapStyle(xlCf.Style, theme)
                };
                sheet.ConditionalFormats.Add(fmt);
            }
        }
    }

    public static void Save(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (!Enum.IsDefined(cf.RuleType) || !Enum.IsDefined(cf.Operator))
                continue;
            if (cf.RuleType is not (CfRuleType.CellValue or CfRuleType.Formula))
                continue;
            if (cf.FormatIfTrue is null && cf.RuleType != CfRuleType.ColorScale && cf.RuleType != CfRuleType.DataBar)
                continue;

            // Apply the rule to every range (primary + additional) so multi-range basic CF
            // rules preserve all ranges instead of losing every range after the first.
            foreach (var range in cf.AllRanges)
            {
                var rangeStr = $"{CellAddress.NumberToColumnName(range.Start.Col)}{range.Start.Row}" +
                               $":{CellAddress.NumberToColumnName(range.End.Col)}{range.End.Row}";
                try
                {
                    var xlRange = xlSheet.Range(rangeStr);
                    var xlCf = xlRange.AddConditionalFormat();

                    if (cf.RuleType == CfRuleType.Formula && !string.IsNullOrWhiteSpace(cf.FormulaText))
                    {
                        var xlStyle = xlCf.WhenIsTrue("=" + cf.FormulaText);
                        xlCf.SetStopIfTrue(cf.StopIfTrue);
                        if (cf.FormatIfTrue is not null)
                            ApplyStyle(xlStyle, cf.FormatIfTrue);
                    }
                    else if (cf.RuleType == CfRuleType.CellValue)
                    {
                        var v1 = FormatCellValueOperand(cf.Value1);
                        var v2 = FormatCellValueOperand(cf.Value2);
                        IXLStyle xlStyle = cf.Operator switch
                        {
                            CfOperator.Equal => xlCf.WhenEquals(v1),
                            CfOperator.NotEqual => xlCf.WhenNotEquals(v1),
                            CfOperator.GreaterThan => xlCf.WhenGreaterThan(v1),
                            CfOperator.GreaterThanOrEqual => xlCf.WhenEqualOrGreaterThan(v1),
                            CfOperator.LessThan => xlCf.WhenLessThan(v1),
                            CfOperator.LessThanOrEqual => xlCf.WhenEqualOrLessThan(v1),
                            CfOperator.Between => xlCf.WhenBetween(v1, v2),
                            CfOperator.NotBetween => xlCf.WhenNotBetween(v1, v2),
                            _ => throw new InvalidOperationException("Unsupported conditional format operator.")
                        };
                        xlCf.SetStopIfTrue(cf.StopIfTrue);
                        if (cf.FormatIfTrue is not null)
                            ApplyStyle(xlStyle, cf.FormatIfTrue);
                    }
                }
                catch
                {
                    // Skip ranges that can't be serialized.
                }
            }
        }
    }

    /// <summary>
    /// Prepares a CellIs rule's Value1/Value2 threshold text for ClosedXML's string-typed
    /// <c>WhenEquals</c>/<c>WhenBetween</c>/etc. API.
    /// <para>
    /// ClosedXML's own CellIs writer (<c>XLCFCellIsConverter.GetQuoted</c>) leaves a threshold
    /// unquoted only when it is a plain number, already wrapped in literal quotes, or flagged
    /// <c>IsFormula</c> (which <see cref="ClosedXML.Excel.XLFormula.Value"/> sets only when the raw
    /// text carries a leading '='); anything else -- most notably a cell reference like
    /// <c>$B$1</c> or a formula expression -- gets silently wrapped in a dead string literal
    /// instead of being written as a live formula operand. FreeX's own threshold text (whether
    /// typed by the user or round-tripped from a real Excel file, where OOXML's CellIs
    /// <c>&lt;formula&gt;</c> grammar never carries the leading '=' itself) never includes that
    /// prefix, so every reference/formula threshold hit this quoting trap. Prefixing a leading '='
    /// here makes ClosedXML mark it <c>IsFormula</c> so it is written unquoted as a real formula
    /// operand, matching real Excel's own CellIs semantics (and this same reference/formula-vs-
    /// literal distinction that <c>ViewportConditionalFormatEvaluator</c> already applies when
    /// evaluating Value1/Value2 in memory).
    /// </para>
    /// </summary>
    private static string FormatCellValueOperand(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw ?? "";

        // Must match ClosedXML's own numeric check in XLCFCellIsConverter.GetQuoted exactly
        // (NumberStyles.Float, invariant culture) -- a looser style here (e.g. NumberStyles.Any,
        // which also accepts thousands separators/parentheses) could classify a value as numeric
        // when ClosedXML's own stricter check would not, leaving it to fall through to the same
        // quoting bug this fix is closing.
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            return raw;

        // Already a quoted text literal (e.g. carried over from a prior round-trip) -- leave as-is
        // so ClosedXML's own quoting logic passes it through unchanged instead of double-quoting it.
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return raw;

        return raw[0] == '=' ? raw : "=" + raw;
    }

    private static CfOperator? MapOperator(XLCFOperator op) => op switch
    {
        XLCFOperator.Equal => CfOperator.Equal,
        XLCFOperator.NotEqual => CfOperator.NotEqual,
        XLCFOperator.GreaterThan => CfOperator.GreaterThan,
        XLCFOperator.EqualOrGreaterThan => CfOperator.GreaterThanOrEqual,
        XLCFOperator.LessThan => CfOperator.LessThan,
        XLCFOperator.EqualOrLessThan => CfOperator.LessThanOrEqual,
        XLCFOperator.Between => CfOperator.Between,
        XLCFOperator.NotBetween => CfOperator.NotBetween,
        _ => (CfOperator?)null
    };

    private static void ApplyStyle(IXLStyle xlStyle, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlStyle.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlStyle.Font.Italic = style.Italic;
        if (style.Strikethrough != def.Strikethrough) xlStyle.Font.Strikethrough = style.Strikethrough;
        if (style.Underline != def.Underline || style.DoubleUnderline != def.DoubleUnderline)
            xlStyle.Font.Underline = style.DoubleUnderline
                ? XLFontUnderlineValues.Double
                : style.Underline
                    ? XLFontUnderlineValues.Single
                    : XLFontUnderlineValues.None;
        if (style.FontColor != def.FontColor)
            xlStyle.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillPatternStyle != CellFillPatternStyle.None)
        {
            xlStyle.Fill.PatternType = style.FillPatternStyle switch
            {
                CellFillPatternStyle.Solid => XLFillPatternValues.Solid,
                CellFillPatternStyle.Gray0625 => XLFillPatternValues.Gray0625,
                CellFillPatternStyle.Gray125 => XLFillPatternValues.Gray125,
                CellFillPatternStyle.LightGray => XLFillPatternValues.LightGray,
                CellFillPatternStyle.MediumGray => XLFillPatternValues.MediumGray,
                CellFillPatternStyle.DarkGray => XLFillPatternValues.DarkGray,
                CellFillPatternStyle.LightHorizontal => XLFillPatternValues.LightHorizontal,
                CellFillPatternStyle.LightVertical => XLFillPatternValues.LightVertical,
                CellFillPatternStyle.LightDown => XLFillPatternValues.LightDown,
                CellFillPatternStyle.LightUp => XLFillPatternValues.LightUp,
                CellFillPatternStyle.LightGrid => XLFillPatternValues.LightGrid,
                CellFillPatternStyle.LightTrellis => XLFillPatternValues.LightTrellis,
                CellFillPatternStyle.DarkHorizontal => XLFillPatternValues.DarkHorizontal,
                CellFillPatternStyle.DarkVertical => XLFillPatternValues.DarkVertical,
                CellFillPatternStyle.DarkDown => XLFillPatternValues.DarkDown,
                CellFillPatternStyle.DarkUp => XLFillPatternValues.DarkUp,
                CellFillPatternStyle.DarkGrid => XLFillPatternValues.DarkGrid,
                CellFillPatternStyle.DarkTrellis => XLFillPatternValues.DarkTrellis,
                _ => XLFillPatternValues.None
            };
            if (style.FillColor.HasValue)
                xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255,
                    style.FillColor.Value.R,
                    style.FillColor.Value.G,
                    style.FillColor.Value.B);
            if (style.FillPatternColor.HasValue)
                xlStyle.Fill.PatternColor = XLColor.FromArgb(255,
                    style.FillPatternColor.Value.R,
                    style.FillPatternColor.Value.G,
                    style.FillPatternColor.Value.B);
        }
        else if (style.FillColor.HasValue)
        {
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255,
                style.FillColor.Value.R,
                style.FillColor.Value.G,
                style.FillColor.Value.B);
        }

        // dxf number format: write to ClosedXML if explicitly set (not "General").
        if (!string.IsNullOrEmpty(style.NumberFormat) &&
            !string.Equals(style.NumberFormat, "General", StringComparison.OrdinalIgnoreCase))
        {
            xlStyle.NumberFormat.Format = style.NumberFormat;
        }

        // dxf borders: write each edge that has a non-None style.
        ApplyBorderEdge(xlStyle.Border, style.BorderTop, "top");
        ApplyBorderEdge(xlStyle.Border, style.BorderRight, "right");
        ApplyBorderEdge(xlStyle.Border, style.BorderBottom, "bottom");
        ApplyBorderEdge(xlStyle.Border, style.BorderLeft, "left");

        if (style.BorderDiagonalDown.Style != BorderStyle.None || style.BorderDiagonalUp.Style != BorderStyle.None)
        {
            // OOXML: diagonal border style/color is shared; diagonalDown/diagonalUp flags select which lines to draw.
            var diagBorder = style.BorderDiagonalDown.Style != BorderStyle.None ? style.BorderDiagonalDown : style.BorderDiagonalUp;
            xlStyle.Border.DiagonalBorder = MapBorderStyleInverse(diagBorder.Style);
            xlStyle.Border.DiagonalBorderColor = XLColor.FromArgb(255, diagBorder.Color.R, diagBorder.Color.G, diagBorder.Color.B);
            xlStyle.Border.DiagonalDown = style.BorderDiagonalDown.Style != BorderStyle.None;
            xlStyle.Border.DiagonalUp = style.BorderDiagonalUp.Style != BorderStyle.None;
        }
    }

    private static void ApplyBorderEdge(IXLBorder xlBorder, CellBorder edge, string side)
    {
        if (edge.Style == BorderStyle.None)
            return;

        var xlStyle = MapBorderStyleInverse(edge.Style);

        var xlColor = XLColor.FromArgb(255, edge.Color.R, edge.Color.G, edge.Color.B);

        switch (side)
        {
            case "top":
                xlBorder.TopBorder = xlStyle;
                xlBorder.TopBorderColor = xlColor;
                break;
            case "right":
                xlBorder.RightBorder = xlStyle;
                xlBorder.RightBorderColor = xlColor;
                break;
            case "bottom":
                xlBorder.BottomBorder = xlStyle;
                xlBorder.BottomBorderColor = xlColor;
                break;
            case "left":
                xlBorder.LeftBorder = xlStyle;
                xlBorder.LeftBorderColor = xlColor;
                break;
        }
    }

    private static XLBorderStyleValues MapBorderStyleInverse(BorderStyle style) => style switch
    {
        BorderStyle.Thin => XLBorderStyleValues.Thin,
        BorderStyle.Medium => XLBorderStyleValues.Medium,
        BorderStyle.Thick => XLBorderStyleValues.Thick,
        BorderStyle.Dashed => XLBorderStyleValues.Dashed,
        BorderStyle.Dotted => XLBorderStyleValues.Dotted,
        BorderStyle.Double => XLBorderStyleValues.Double,
        BorderStyle.Hair => XLBorderStyleValues.Hair,
        BorderStyle.SlantDashDot => XLBorderStyleValues.SlantDashDot,
        BorderStyle.MediumDashed => XLBorderStyleValues.MediumDashed,
        BorderStyle.DashDot => XLBorderStyleValues.DashDot,
        BorderStyle.MediumDashDot => XLBorderStyleValues.MediumDashDot,
        BorderStyle.DashDotDot => XLBorderStyleValues.DashDotDot,
        BorderStyle.MediumDashDotDot => XLBorderStyleValues.MediumDashDotDot,
        _ => XLBorderStyleValues.None,
    };
}
