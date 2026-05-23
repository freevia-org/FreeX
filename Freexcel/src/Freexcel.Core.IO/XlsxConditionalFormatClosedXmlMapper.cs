using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxConditionalFormatClosedXmlMapper
{
    public static void Load(
        IXLWorksheet xlSheet,
        Sheet sheet,
        WorkbookTheme theme,
        Func<IXLStyle, WorkbookTheme, CellStyle> mapStyle)
    {
        int priority = 1;
        foreach (var xlCf in xlSheet.ConditionalFormats)
        {
            var xlRange = xlCf.Range;
            var sheetId = sheet.Id;
            var start = new CellAddress(sheetId,
                (uint)xlRange.RangeAddress.FirstAddress.RowNumber,
                (uint)xlRange.RangeAddress.FirstAddress.ColumnNumber);
            var end = new CellAddress(sheetId,
                (uint)xlRange.RangeAddress.LastAddress.RowNumber,
                (uint)xlRange.RangeAddress.LastAddress.ColumnNumber);
            var appliesTo = new GridRange(start, end);

            if (xlCf.ConditionalFormatType == XLConditionalFormatType.CellIs)
            {
                var op = MapOperator(xlCf.Operator);
                if (op is null)
                {
                    priority++;
                    continue;
                }

                var values = xlCf.Values;
                string? v1 = values.TryGetValue(1, out var xv1) ? xv1.Value : null;
                string? v2 = values.TryGetValue(2, out var xv2) ? xv2.Value : null;

                var fmt = new ConditionalFormat
                {
                    AppliesTo = appliesTo,
                    Priority = priority++,
                    RuleType = CfRuleType.CellValue,
                    Operator = op.Value,
                    Value1 = v1,
                    Value2 = v2,
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
                    priority++;
                    continue;
                }

                if (formula.StartsWith('='))
                    formula = formula[1..];

                var fmt = new ConditionalFormat
                {
                    AppliesTo = appliesTo,
                    Priority = priority++,
                    RuleType = CfRuleType.Formula,
                    FormulaText = formula,
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

            var rangeStr = $"{CellAddress.NumberToColumnName(cf.AppliesTo.Start.Col)}{cf.AppliesTo.Start.Row}" +
                           $":{CellAddress.NumberToColumnName(cf.AppliesTo.End.Col)}{cf.AppliesTo.End.Row}";

            try
            {
                var xlRange = xlSheet.Range(rangeStr);
                var xlCf = xlRange.AddConditionalFormat();

                if (cf.RuleType == CfRuleType.Formula && !string.IsNullOrWhiteSpace(cf.FormulaText))
                {
                    var xlStyle = xlCf.WhenIsTrue("=" + cf.FormulaText);
                    if (cf.FormatIfTrue is not null)
                        ApplyStyle(xlStyle, cf.FormatIfTrue);
                }
                else if (cf.RuleType == CfRuleType.CellValue)
                {
                    var v1 = cf.Value1 ?? "";
                    var v2 = cf.Value2 ?? "";
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
                    if (cf.FormatIfTrue is not null)
                        ApplyStyle(xlStyle, cf.FormatIfTrue);
                }
            }
            catch
            {
                // Skip rules that can't be serialized.
            }
        }
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
        if (style.Underline != def.Underline)
            xlStyle.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
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
    }
}
