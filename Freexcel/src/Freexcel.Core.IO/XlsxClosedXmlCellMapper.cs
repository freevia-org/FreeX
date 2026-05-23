using System.Reflection;
using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxClosedXmlCellMapper
{
    private static readonly FieldInfo? XlCellValueNumberField =
        typeof(XLCellValue).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);

    public static ScalarValue MapValue(IXLCell xlCell)
    {
        if (xlCell.Value.IsDateTime)
        {
            try { return DateTimeValue.FromDateTime(xlCell.GetDateTime()); }
            catch (ArgumentException)
            {
                return TryGetUnifiedNumber(xlCell.Value, out var serial)
                    ? new NumberValue(serial)
                    : ErrorValue.Num;
            }
        }

        return MapValue(xlCell.Value);
    }

    public static string NormalizeFormulaText(string formulaText)
    {
        return formulaText
            .Replace("_xlfn.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_xlws.", "", StringComparison.OrdinalIgnoreCase);
    }

    public static ScalarValue MapValue(XLCellValue xlValue)
    {
        if (xlValue.IsBlank) return BlankValue.Instance;
        if (xlValue.IsNumber) return new NumberValue(xlValue.GetNumber());
        if (xlValue.IsText) return new TextValue(xlValue.GetText());
        if (xlValue.IsBoolean) return new BoolValue(xlValue.GetBoolean());
        if (xlValue.IsDateTime)
        {
            try { return DateTimeValue.FromDateTime(xlValue.GetDateTime()); }
            catch (ArgumentException)
            {
                try { return new NumberValue(xlValue.GetNumber()); }
                catch { return ErrorValue.Num; }
            }
        }
        if (xlValue.IsError) return MapErrorValue(xlValue.GetError());
        return new TextValue(xlValue.ToString());
    }

    public static XLCellValue MapValueInverse(ScalarValue value) => value switch
    {
        NumberValue n => n.Value,
        TextValue t => t.Value,
        BoolValue b => b.Value,
        DateTimeValue dt => DateTime.FromOADate(dt.Value),
        ErrorValue e => MapErrorValueInverse(e),
        _ => Blank.Value
    };

    public static CellStyle MapStyle(IXLStyle xlStyle, WorkbookTheme theme)
    {
        return new CellStyle
        {
            FontName = xlStyle.Font.FontName,
            FontSize = IsSupportedFontSize(xlStyle.Font.FontSize)
                ? xlStyle.Font.FontSize
                : CellStyle.Default.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            Strikethrough = xlStyle.Font.Strikethrough,
            FontColor = MapColor(xlStyle.Font.FontColor, theme),
            FillColor = xlStyle.Fill.PatternType != XLFillPatternValues.None
                ? (CellColor?)MapColor(xlStyle.Fill.BackgroundColor, theme)
                : null,
            FillPatternStyle = MapFillPatternStyle(xlStyle.Fill.PatternType),
            FillPatternColor = xlStyle.Fill.PatternType is XLFillPatternValues.None or XLFillPatternValues.Solid
                ? null
                : MapColor(xlStyle.Fill.PatternColor, theme),
            BorderTop = MapBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor, theme),
            BorderRight = MapBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor, theme),
            BorderBottom = MapBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor, theme),
            BorderLeft = MapBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor, theme),
            NumberFormat = string.IsNullOrEmpty(xlStyle.NumberFormat.Format) ? "General" : xlStyle.NumberFormat.Format,
            HorizontalAlignment = xlStyle.Alignment.Horizontal switch
            {
                XLAlignmentHorizontalValues.General => HorizontalAlignment.General,
                XLAlignmentHorizontalValues.Left => HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
                XLAlignmentHorizontalValues.Justify => HorizontalAlignment.Justify,
                XLAlignmentHorizontalValues.Distributed => HorizontalAlignment.Distributed,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
                XLAlignmentVerticalValues.Justify => VerticalAlignment.Justify,
                XLAlignmentVerticalValues.Distributed => VerticalAlignment.Distributed,
                _ => VerticalAlignment.Bottom,
            },
            WrapText = xlStyle.Alignment.WrapText,
            ShrinkToFit = xlStyle.Alignment.ShrinkToFit,
            TextRotation = IsSupportedTextRotation(xlStyle.Alignment.TextRotation)
                ? xlStyle.Alignment.TextRotation
                : 0,
            Locked = xlStyle.Protection.Locked,
        };
    }

    public static void ApplyStyle(IXLCell xlCell, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlCell.Style.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlCell.Style.Font.Italic = style.Italic;
        if (style.Underline != def.Underline)
            xlCell.Style.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        if (style.Strikethrough != def.Strikethrough)
            xlCell.Style.Font.Strikethrough = style.Strikethrough;
        if (style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize))
            xlCell.Style.Font.FontSize = style.FontSize;
        if (style.FontName != def.FontName) xlCell.Style.Font.FontName = style.FontName;
        if (style.FontColor != def.FontColor)
            xlCell.Style.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillPatternStyle != CellFillPatternStyle.None)
        {
            xlCell.Style.Fill.PatternType = MapFillPatternStyleInverse(style.FillPatternStyle);
            if (style.FillColor.HasValue)
                xlCell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, style.FillColor.Value.R, style.FillColor.Value.G, style.FillColor.Value.B);
            if (style.FillPatternColor.HasValue)
                xlCell.Style.Fill.PatternColor = XLColor.FromArgb(255, style.FillPatternColor.Value.R, style.FillPatternColor.Value.G, style.FillPatternColor.Value.B);
        }
        else if (style.FillColor.HasValue)
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
                HorizontalAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                HorizontalAlignment.Distributed => XLAlignmentHorizontalValues.Distributed,
                _ => XLAlignmentHorizontalValues.General,
            };

        if (style.VerticalAlignment != def.VerticalAlignment)
            xlCell.Style.Alignment.Vertical = style.VerticalAlignment switch
            {
                VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                VerticalAlignment.Justify => XLAlignmentVerticalValues.Justify,
                VerticalAlignment.Distributed => XLAlignmentVerticalValues.Distributed,
                _ => XLAlignmentVerticalValues.Bottom,
            };

        if (style.WrapText != def.WrapText)
            xlCell.Style.Alignment.WrapText = style.WrapText;

        if (style.ShrinkToFit != def.ShrinkToFit)
            xlCell.Style.Alignment.ShrinkToFit = style.ShrinkToFit;

        if (style.TextRotation != def.TextRotation && IsSupportedTextRotation(style.TextRotation))
            xlCell.Style.Alignment.TextRotation = style.TextRotation;

        if (style.NumberFormat != def.NumberFormat)
            xlCell.Style.NumberFormat.Format = style.NumberFormat;

        if (style.Locked != def.Locked)
            xlCell.Style.Protection.Locked = style.Locked;
    }

    private static bool TryGetUnifiedNumber(XLCellValue value, out double number)
    {
        number = 0;
        if (!value.IsUnifiedNumber || XlCellValueNumberField is null)
            return false;

        if (XlCellValueNumberField.GetValue(value) is double raw)
        {
            number = raw;
            return true;
        }

        return false;
    }

    private static ErrorValue MapErrorValue(XLError error) => error switch
    {
        XLError.NullValue => ErrorValue.Null,
        XLError.DivisionByZero => ErrorValue.DivByZero,
        XLError.IncompatibleValue => ErrorValue.Value,
        XLError.CellReference => ErrorValue.Ref,
        XLError.NameNotRecognized => ErrorValue.Name,
        XLError.NumberInvalid => ErrorValue.Num,
        XLError.NoValueAvailable => ErrorValue.NA,
        _ => new ErrorValue(error.ToString())
    };

    private static XLError MapErrorValueInverse(ErrorValue error) => error.Code.ToUpperInvariant() switch
    {
        "#NULL!" => XLError.NullValue,
        "#DIV/0!" => XLError.DivisionByZero,
        "#VALUE!" => XLError.IncompatibleValue,
        "#REF!" => XLError.CellReference,
        "#NAME?" => XLError.NameNotRecognized,
        "#NUM!" => XLError.NumberInvalid,
        "#N/A" => XLError.NoValueAvailable,
        _ => XLError.NoValueAvailable
    };

    private static CellColor MapColor(XLColor xlColor, WorkbookTheme theme)
    {
        if (xlColor.ColorType == XLColorType.Theme)
            return theme.ResolveColor(ToWorkbookThemeColorSlot(xlColor.ThemeColor), xlColor.ThemeTint);

        System.Drawing.Color c;
        try
        {
            c = xlColor.Color;
        }
        catch (InvalidOperationException)
        {
            return new CellColor(0, 0, 0);
        }

        return new CellColor(c.R, c.G, c.B);
    }

    private static WorkbookThemeColorSlot ToWorkbookThemeColorSlot(XLThemeColor themeColor) => themeColor switch
    {
        XLThemeColor.Text1 => WorkbookThemeColorSlot.Dark1,
        XLThemeColor.Background1 => WorkbookThemeColorSlot.Light1,
        XLThemeColor.Text2 => WorkbookThemeColorSlot.Dark2,
        XLThemeColor.Background2 => WorkbookThemeColorSlot.Light2,
        XLThemeColor.Accent1 => WorkbookThemeColorSlot.Accent1,
        XLThemeColor.Accent2 => WorkbookThemeColorSlot.Accent2,
        XLThemeColor.Accent3 => WorkbookThemeColorSlot.Accent3,
        XLThemeColor.Accent4 => WorkbookThemeColorSlot.Accent4,
        XLThemeColor.Accent5 => WorkbookThemeColorSlot.Accent5,
        XLThemeColor.Accent6 => WorkbookThemeColorSlot.Accent6,
        XLThemeColor.Hyperlink => WorkbookThemeColorSlot.Hyperlink,
        XLThemeColor.FollowedHyperlink => WorkbookThemeColorSlot.FollowedHyperlink,
        _ => WorkbookThemeColorSlot.Dark1
    };

    private static CellBorder MapBorder(XLBorderStyleValues style, XLColor color, WorkbookTheme theme)
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
        return new CellBorder(mapped, MapColor(color, theme));
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

    private static CellFillPatternStyle MapFillPatternStyle(XLFillPatternValues pattern) => pattern switch
    {
        XLFillPatternValues.Solid => CellFillPatternStyle.Solid,
        XLFillPatternValues.Gray0625 => CellFillPatternStyle.Gray0625,
        XLFillPatternValues.Gray125 => CellFillPatternStyle.Gray125,
        XLFillPatternValues.LightGray => CellFillPatternStyle.LightGray,
        XLFillPatternValues.MediumGray => CellFillPatternStyle.MediumGray,
        XLFillPatternValues.DarkGray => CellFillPatternStyle.DarkGray,
        XLFillPatternValues.LightHorizontal => CellFillPatternStyle.LightHorizontal,
        XLFillPatternValues.LightVertical => CellFillPatternStyle.LightVertical,
        XLFillPatternValues.LightDown => CellFillPatternStyle.LightDown,
        XLFillPatternValues.LightUp => CellFillPatternStyle.LightUp,
        XLFillPatternValues.LightGrid => CellFillPatternStyle.LightGrid,
        XLFillPatternValues.LightTrellis => CellFillPatternStyle.LightTrellis,
        XLFillPatternValues.DarkHorizontal => CellFillPatternStyle.DarkHorizontal,
        XLFillPatternValues.DarkVertical => CellFillPatternStyle.DarkVertical,
        XLFillPatternValues.DarkDown => CellFillPatternStyle.DarkDown,
        XLFillPatternValues.DarkUp => CellFillPatternStyle.DarkUp,
        XLFillPatternValues.DarkGrid => CellFillPatternStyle.DarkGrid,
        XLFillPatternValues.DarkTrellis => CellFillPatternStyle.DarkTrellis,
        _ => CellFillPatternStyle.None,
    };

    private static XLFillPatternValues MapFillPatternStyleInverse(CellFillPatternStyle pattern) => pattern switch
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
        _ => XLFillPatternValues.None,
    };

    private static bool IsSupportedTextRotation(int rotation) =>
        (rotation >= -90 && rotation <= 90) || rotation == 255;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize > 0 && fontSize <= 409;
}
