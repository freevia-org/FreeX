using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// XLSX file adapter using ClosedXML.
/// Supports standard Excel .xlsx files.
/// </summary>
public sealed class XlsxFileAdapter : IFileAdapter
{
    public string Extension => ".xlsx";
    public string FormatName => "Excel Workbook";

    public Workbook Load(Stream stream)
    {
        using var xlWorkbook = new XLWorkbook(stream);
        var workbook = new Workbook("Untitled");

        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.AddSheet(xlSheet.Name);

            foreach (var xlCell in xlSheet.CellsUsed())
            {
                var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);

                Cell cell;
                if (xlCell.HasFormula)
                {
                    cell = Cell.FromFormula(xlCell.FormulaA1);
                    // Preserve the cached formula result so callers see the last-calculated value
                    // without needing to recalculate immediately.
                    var cached = MapValue(xlCell.Value);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                }
                else
                {
                    cell = Cell.FromValue(MapValue(xlCell.Value));
                }

                var style = MapStyle(xlCell.Style);
                if (!style.Equals(CellStyle.Default))
                    cell.StyleId = workbook.RegisterStyle(style);

                sheet.SetCell(addr, cell);
            }

            foreach (var row in xlSheet.RowsUsed())
                if (row.Height > 0)
                    sheet.RowHeights[(uint)row.RowNumber()] = row.Height * (96.0 / 72.0);

            foreach (var col in xlSheet.ColumnsUsed())
                if (col.Width > 0)
                    sheet.ColumnWidths[(uint)col.ColumnNumber()] = col.Width;

            if (xlSheet.SheetView.SplitRow > 0)
                sheet.FrozenRows = (uint)xlSheet.SheetView.SplitRow;
            if (xlSheet.SheetView.SplitColumn > 0)
                sheet.FrozenCols = (uint)xlSheet.SheetView.SplitColumn;

            // Load CellIs conditional format rules (best-effort; skip anything we can't map)
            try { LoadConditionalFormats(xlSheet, sheet, workbook); }
            catch { /* ignore CF load failures */ }

            // Load data validation rules (best-effort)
            try { LoadDataValidations(xlSheet, sheet); }
            catch { /* ignore DV load failures */ }
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        using var xlWorkbook = new XLWorkbook();

        foreach (var sheet in workbook.Sheets)
        {
            var xlSheet = xlWorkbook.Worksheets.Add(sheet.Name);

            foreach (var pair in sheet.GetUsedCells())
            {
                var cell = pair.Value;

                // Skip blank cells that carry no style
                if (cell.Value is BlankValue && !cell.HasFormula && cell.StyleId == StyleId.Default)
                    continue;

                var xlCell = xlSheet.Cell((int)pair.Key.Row, (int)pair.Key.Col);

                if (cell.HasFormula)
                {
                    xlCell.FormulaA1 = cell.FormulaText;
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = MapValueInverse(cell.Value);
                }

                var style = workbook.GetStyle(cell.StyleId);
                ApplyStyle(xlCell, style);
            }

            foreach (var (rowNum, height) in sheet.RowHeights)
                xlSheet.Row((int)rowNum).Height = height * (72.0 / 96.0);

            foreach (var (colNum, width) in sheet.ColumnWidths)
                xlSheet.Column((int)colNum).Width = width;

            if (sheet.FrozenRows > 0 || sheet.FrozenCols > 0)
                xlSheet.SheetView.Freeze((int)sheet.FrozenRows, (int)sheet.FrozenCols);

            // Save CellValue conditional format rules back to XLSX
            SaveConditionalFormats(sheet, xlSheet);

            // Save data validation rules back to XLSX
            try { SaveDataValidations(sheet, xlSheet); }
            catch { /* ignore DV save failures */ }
        }

        xlWorkbook.SaveAs(stream);
    }

    private static ScalarValue MapValue(XLCellValue xlValue)
    {
        if (xlValue.IsBlank) return BlankValue.Instance;
        if (xlValue.IsNumber) return new NumberValue(xlValue.GetNumber());
        if (xlValue.IsText) return new TextValue(xlValue.GetText());
        if (xlValue.IsBoolean) return new BoolValue(xlValue.GetBoolean());
        if (xlValue.IsDateTime) return DateTimeValue.FromDateTime(xlValue.GetDateTime());
        if (xlValue.IsError) return new ErrorValue(xlValue.GetError().ToString());
        return new TextValue(xlValue.ToString());
    }

    private static XLCellValue MapValueInverse(ScalarValue value) => value switch
    {
        NumberValue n => n.Value,
        TextValue t => t.Value,
        BoolValue b => b.Value,
        DateTimeValue dt => DateTime.FromOADate(dt.Value),
        ErrorValue => XLError.NoValueAvailable,
        _ => Blank.Value
    };

    private static CellStyle MapStyle(IXLStyle xlStyle)
    {
        return new CellStyle
        {
            FontName = xlStyle.Font.FontName,
            FontSize = xlStyle.Font.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            FontColor = MapColor(xlStyle.Font.FontColor),
            FillColor = xlStyle.Fill.PatternType == XLFillPatternValues.Solid
                ? (CellColor?)MapColor(xlStyle.Fill.BackgroundColor)
                : null,
            BorderTop = MapBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor),
            BorderRight = MapBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor),
            BorderBottom = MapBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor),
            BorderLeft = MapBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor),
            // ClosedXML returns empty string for built-in format ID 0 (General) and some
            // other built-in IDs. Phase 2 limitation: built-in IDs without an explicit
            // format string are treated as General.
            NumberFormat = string.IsNullOrEmpty(xlStyle.NumberFormat.Format) ? "General" : xlStyle.NumberFormat.Format,
            HorizontalAlignment = xlStyle.Alignment.Horizontal switch
            {
                XLAlignmentHorizontalValues.General => HorizontalAlignment.General,
                XLAlignmentHorizontalValues.Left => HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Bottom,
            },
            WrapText = xlStyle.Alignment.WrapText,
        };
    }

    private static CellColor MapColor(XLColor xlColor)
    {
        if (xlColor.ColorType == XLColorType.Color)
            return new CellColor(xlColor.Color.R, xlColor.Color.G, xlColor.Color.B);
        // Theme and indexed colors require workbook theme context to resolve to RGB.
        // Phase 2 limitation: flattened to black. Track as a known gap.
        return CellColor.Black;
    }

    private static CellBorder MapBorder(XLBorderStyleValues style, XLColor color)
    {
        var mapped = style switch
        {
            XLBorderStyleValues.None => BorderStyle.None,
            XLBorderStyleValues.Thin => BorderStyle.Thin,
            XLBorderStyleValues.Medium => BorderStyle.Medium,
            XLBorderStyleValues.Thick => BorderStyle.Thick,
            XLBorderStyleValues.Dashed => BorderStyle.Dashed,
            XLBorderStyleValues.Dotted => BorderStyle.Dotted,
            XLBorderStyleValues.Double => BorderStyle.Double,
            _ => BorderStyle.None,
        };
        return new CellBorder(mapped, MapColor(color));
    }

    private static void ApplyStyle(IXLCell xlCell, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlCell.Style.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlCell.Style.Font.Italic = style.Italic;
        if (style.Underline != def.Underline)
            xlCell.Style.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        if (style.FontSize != def.FontSize) xlCell.Style.Font.FontSize = style.FontSize;
        if (style.FontName != def.FontName) xlCell.Style.Font.FontName = style.FontName;
        if (style.FontColor != def.FontColor)
            xlCell.Style.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillColor.HasValue)
        {
            xlCell.Style.Fill.PatternType = XLFillPatternValues.Solid;
            xlCell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, style.FillColor.Value.R, style.FillColor.Value.G, style.FillColor.Value.B);
        }

        if (style.BorderTop.Style != BorderStyle.None)
        {
            xlCell.Style.Border.TopBorder = MapBorderStyleInverse(style.BorderTop.Style);
            xlCell.Style.Border.TopBorderColor = XLColor.FromArgb(255, style.BorderTop.Color.R, style.BorderTop.Color.G, style.BorderTop.Color.B);
        }
        if (style.BorderRight.Style != BorderStyle.None)
        {
            xlCell.Style.Border.RightBorder = MapBorderStyleInverse(style.BorderRight.Style);
            xlCell.Style.Border.RightBorderColor = XLColor.FromArgb(255, style.BorderRight.Color.R, style.BorderRight.Color.G, style.BorderRight.Color.B);
        }
        if (style.BorderBottom.Style != BorderStyle.None)
        {
            xlCell.Style.Border.BottomBorder = MapBorderStyleInverse(style.BorderBottom.Style);
            xlCell.Style.Border.BottomBorderColor = XLColor.FromArgb(255, style.BorderBottom.Color.R, style.BorderBottom.Color.G, style.BorderBottom.Color.B);
        }
        if (style.BorderLeft.Style != BorderStyle.None)
        {
            xlCell.Style.Border.LeftBorder = MapBorderStyleInverse(style.BorderLeft.Style);
            xlCell.Style.Border.LeftBorderColor = XLColor.FromArgb(255, style.BorderLeft.Color.R, style.BorderLeft.Color.G, style.BorderLeft.Color.B);
        }

        if (style.HorizontalAlignment != def.HorizontalAlignment)
            xlCell.Style.Alignment.Horizontal = style.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => XLAlignmentHorizontalValues.Left,
                HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
                HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
                _ => XLAlignmentHorizontalValues.General,
            };

        if (style.VerticalAlignment != def.VerticalAlignment)
            xlCell.Style.Alignment.Vertical = style.VerticalAlignment switch
            {
                VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                _ => XLAlignmentVerticalValues.Bottom,
            };

        if (style.WrapText != def.WrapText)
            xlCell.Style.Alignment.WrapText = style.WrapText;

        if (style.NumberFormat != def.NumberFormat)
            xlCell.Style.NumberFormat.Format = style.NumberFormat;
    }

    // ── Conditional formatting load ────────────────────────────────────────────

    private static void LoadConditionalFormats(IXLWorksheet xlSheet, Sheet sheet, Workbook workbook)
    {
        int priority = 1;
        foreach (var xlCf in xlSheet.ConditionalFormats)
        {
            // Map the range
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
                if (op is null) { priority++; continue; }

                var values = xlCf.Values;
                string? v1 = values.TryGetValue(1, out var xv1) ? xv1.Value : null;
                string? v2 = values.TryGetValue(2, out var xv2) ? xv2.Value : null;

                var fmt = new ConditionalFormat
                {
                    AppliesTo    = appliesTo,
                    Priority     = priority++,
                    RuleType     = CfRuleType.CellValue,
                    Operator     = op.Value,
                    Value1       = v1,
                    Value2       = v2,
                    FormatIfTrue = MapStyle(xlCf.Style)
                };
                sheet.ConditionalFormats.Add(fmt);
            }
            // ColorScale, DataBar etc. are intentionally skipped on load for v1
        }
    }

    private static CfOperator? MapOperator(XLCFOperator op) => op switch
    {
        XLCFOperator.Equal              => CfOperator.Equal,
        XLCFOperator.NotEqual           => CfOperator.NotEqual,
        XLCFOperator.GreaterThan        => CfOperator.GreaterThan,
        XLCFOperator.EqualOrGreaterThan => CfOperator.GreaterThanOrEqual,
        XLCFOperator.LessThan           => CfOperator.LessThan,
        XLCFOperator.EqualOrLessThan    => CfOperator.LessThanOrEqual,
        XLCFOperator.Between            => CfOperator.Between,
        XLCFOperator.NotBetween         => CfOperator.NotBetween,
        _                               => (CfOperator?)null
    };

    // ── Conditional formatting save ────────────────────────────────────────────

    private static void SaveConditionalFormats(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.CellValue) continue;
            if (cf.FormatIfTrue is null) continue;

            var v1 = cf.Value1 ?? "";
            var v2 = cf.Value2 ?? "";

            var rangeStr = $"{CellAddress.NumberToColumnName(cf.AppliesTo.Start.Col)}{cf.AppliesTo.Start.Row}" +
                           $":{CellAddress.NumberToColumnName(cf.AppliesTo.End.Col)}{cf.AppliesTo.End.Row}";

            try
            {
                var xlRange = xlSheet.Range(rangeStr);
                var xlCf   = xlRange.AddConditionalFormat();

                IXLStyle xlStyle = cf.Operator switch
                {
                    CfOperator.Equal              => xlCf.WhenEquals(v1),
                    CfOperator.NotEqual           => xlCf.WhenNotEquals(v1),
                    CfOperator.GreaterThan        => xlCf.WhenGreaterThan(v1),
                    CfOperator.GreaterThanOrEqual => xlCf.WhenEqualOrGreaterThan(v1),
                    CfOperator.LessThan           => xlCf.WhenLessThan(v1),
                    CfOperator.LessThanOrEqual    => xlCf.WhenEqualOrLessThan(v1),
                    CfOperator.Between            => xlCf.WhenBetween(v1, v2),
                    CfOperator.NotBetween         => xlCf.WhenNotBetween(v1, v2),
                    _                             => xlCf.WhenEquals(v1)
                };
                ApplyCfStyle(xlStyle, cf.FormatIfTrue);
            }
            catch
            {
                // Skip rules that can't be serialized
            }
        }
    }

    /// <summary>Apply a <see cref="CellStyle"/> to an <see cref="IXLStyle"/> (used for CF rules).</summary>
    private static void ApplyCfStyle(IXLStyle xlStyle, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlStyle.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlStyle.Font.Italic = style.Italic;
        if (style.Underline != def.Underline)
            xlStyle.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        if (style.FontColor != def.FontColor)
            xlStyle.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillColor.HasValue)
        {
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255,
                style.FillColor.Value.R,
                style.FillColor.Value.G,
                style.FillColor.Value.B);
        }
    }

    // ── Data validation load ───────────────────────────────────────────────────

    private static void LoadDataValidations(IXLWorksheet xlSheet, Sheet sheet)
    {
        foreach (var xlDv in xlSheet.DataValidations)
        {
            try
            {
                var rangeAddr = xlDv.Ranges.FirstOrDefault()?.RangeAddress;
                if (rangeAddr == null) continue;

                var sheetId = sheet.Id;
                var start = new CellAddress(sheetId,
                    (uint)rangeAddr.FirstAddress.RowNumber,
                    (uint)rangeAddr.FirstAddress.ColumnNumber);
                var end = new CellAddress(sheetId,
                    (uint)rangeAddr.LastAddress.RowNumber,
                    (uint)rangeAddr.LastAddress.ColumnNumber);
                var appliesTo = new GridRange(start, end);

                var dv = new DataValidation
                {
                    AppliesTo    = appliesTo,
                    AllowBlank   = xlDv.IgnoreBlanks,
                    ShowDropdown = !xlDv.InCellDropdown.Equals(false),
                    ErrorTitle   = xlDv.ErrorTitle,
                    ErrorMessage = xlDv.ErrorMessage,
                    PromptTitle  = xlDv.InputTitle,
                    PromptMessage = xlDv.InputMessage,
                };

                // Map type
                dv.Type = xlDv.AllowedValues switch
                {
                    XLAllowedValues.WholeNumber => DvType.WholeNumber,
                    XLAllowedValues.Decimal     => DvType.Decimal,
                    XLAllowedValues.List        => DvType.List,
                    XLAllowedValues.Date        => DvType.Date,
                    XLAllowedValues.Time        => DvType.Time,
                    XLAllowedValues.TextLength  => DvType.TextLength,
                    XLAllowedValues.Custom      => DvType.Custom,
                    _                           => DvType.Any
                };

                // Map operator
                dv.Operator = xlDv.Operator switch
                {
                    XLOperator.Between            => DvOperator.Between,
                    XLOperator.NotBetween         => DvOperator.NotBetween,
                    XLOperator.EqualTo            => DvOperator.Equal,
                    XLOperator.NotEqualTo         => DvOperator.NotEqual,
                    XLOperator.GreaterThan        => DvOperator.GreaterThan,
                    XLOperator.LessThan           => DvOperator.LessThan,
                    XLOperator.EqualOrGreaterThan => DvOperator.GreaterThanOrEqual,
                    XLOperator.EqualOrLessThan    => DvOperator.LessThanOrEqual,
                    _                             => DvOperator.Between
                };

                // Map formula values
                if (dv.Type == DvType.List)
                {
                    // ClosedXML stores list items in MinValue as a quoted formula like "\"A,B,C\""
                    var raw = xlDv.MinValue ?? "";
                    // Strip surrounding quotes if present
                    if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
                        raw = raw.Substring(1, raw.Length - 2);
                    dv.Formula1 = raw.Replace("\"\"", "\"");
                }
                else
                {
                    dv.Formula1 = xlDv.MinValue;
                    dv.Formula2 = xlDv.MaxValue;
                }

                sheet.DataValidations.Add(dv);
            }
            catch
            {
                // Skip any individual validation we can't map
            }
        }
    }

    // ── Data validation save ───────────────────────────────────────────────────

    private static void SaveDataValidations(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var dv in sheet.DataValidations)
        {
            try
            {
                var rangeStr = $"{CellAddress.NumberToColumnName(dv.AppliesTo.Start.Col)}{dv.AppliesTo.Start.Row}" +
                               $":{CellAddress.NumberToColumnName(dv.AppliesTo.End.Col)}{dv.AppliesTo.End.Row}";

                var xlRange = xlSheet.Range(rangeStr);
#pragma warning disable CS0618 // SetDataValidation is obsolete in newer ClosedXML but CreateDataValidation may not exist in 0.105
                var xlDv    = xlRange.CreateDataValidation();
#pragma warning restore CS0618

                xlDv.IgnoreBlanks  = dv.AllowBlank;
                xlDv.InCellDropdown = dv.ShowDropdown;

                if (!string.IsNullOrEmpty(dv.ErrorTitle))   xlDv.ErrorTitle   = dv.ErrorTitle;
                if (!string.IsNullOrEmpty(dv.ErrorMessage)) xlDv.ErrorMessage = dv.ErrorMessage;
                if (!string.IsNullOrEmpty(dv.PromptTitle))  xlDv.InputTitle   = dv.PromptTitle;
                if (!string.IsNullOrEmpty(dv.PromptMessage)) xlDv.InputMessage = dv.PromptMessage;

                var f1 = dv.Formula1 ?? "";
                var f2 = dv.Formula2 ?? "";

                switch (dv.Type)
                {
                    case DvType.List:
                        xlDv.List(f1, dv.ShowDropdown);
                        break;

                    case DvType.WholeNumber:
                        ApplyNumericDv(xlDv.WholeNumber, dv.Operator, f1, f2);
                        break;

                    case DvType.Decimal:
                        ApplyNumericDv(xlDv.Decimal, dv.Operator, f1, f2);
                        break;

                    case DvType.Date:
                        ApplyNumericDv(xlDv.Date, dv.Operator, f1, f2);
                        break;

                    case DvType.Time:
                        ApplyNumericDv(xlDv.Time, dv.Operator, f1, f2);
                        break;

                    case DvType.TextLength:
                        ApplyNumericDv(xlDv.TextLength, dv.Operator, f1, f2);
                        break;

                    case DvType.Custom:
                        xlDv.Custom(f1);
                        break;

                    // DvType.Any — leave as-is (ClosedXML default = no restriction)
                }
            }
            catch
            {
                // Skip rules that can't be serialized
            }
        }
    }

    private static void ApplyNumericDv(IXLValidationCriteria rule, DvOperator op, string f1, string f2)
    {
        switch (op)
        {
            case DvOperator.Between:            rule.Between(f1, f2); break;
            case DvOperator.NotBetween:         rule.NotBetween(f1, f2); break;
            case DvOperator.Equal:              rule.EqualTo(f1); break;
            case DvOperator.NotEqual:           rule.NotEqualTo(f1); break;
            case DvOperator.GreaterThan:        rule.GreaterThan(f1); break;
            case DvOperator.LessThan:           rule.LessThan(f1); break;
            case DvOperator.GreaterThanOrEqual: rule.EqualOrGreaterThan(f1); break;
            case DvOperator.LessThanOrEqual:    rule.EqualOrLessThan(f1); break;
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
        _ => XLBorderStyleValues.None,
    };
}
