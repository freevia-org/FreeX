using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static CellStyle? ToCellStyle(CellStyleDto? dto)
    {
        if (dto is null)
            return null;

        return new CellStyle
        {
            FontName = string.IsNullOrWhiteSpace(dto.FontName) ? CellStyle.Default.FontName : dto.FontName,
            FontSize = NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.FontSize, CellStyle.Default.FontSize),
            Bold = dto.Bold,
            Italic = dto.Italic,
            Underline = dto.Underline,
            Strikethrough = dto.Strikethrough,
            Superscript = dto.Superscript,
            Subscript = dto.Subscript,
            FontColor = dto.FontColor,
            FillColor = dto.FillColor,
            FillPatternStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.FillPatternStyle, CellFillPatternStyle.None),
            FillPatternColor = dto.FillPatternColor,
            BorderTop = ToCellBorder(dto.BorderTop),
            BorderRight = ToCellBorder(dto.BorderRight),
            BorderBottom = ToCellBorder(dto.BorderBottom),
            BorderLeft = ToCellBorder(dto.BorderLeft),
            NumberFormat = string.IsNullOrWhiteSpace(dto.NumberFormat) ? CellStyle.Default.NumberFormat : dto.NumberFormat,
            HorizontalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.HorizontalAlignment, HorizontalAlignment.General),
            VerticalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.VerticalAlignment, VerticalAlignment.Bottom),
            WrapText = dto.WrapText,
            ShrinkToFit = dto.ShrinkToFit,
            DoubleUnderline = dto.DoubleUnderline,
            IndentLevel = Math.Clamp(dto.IndentLevel, 0, 15),
            TextRotation = NativeJsonValueSanitizer.ValidTextRotationOrDefault(dto.TextRotation),
            Locked = dto.Locked,
            Hidden = dto.Hidden,
            NativeDifferentialAttributes = dto.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = dto.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = dto.NativeDifferentialElementXmls
        };
    }

    private static CellStyleDto? FromCellStyle(CellStyle? style)
    {
        if (style is null)
            return null;

        var safeStyle = ToCellStyle(new CellStyleDto
        {
            FontName = style.FontName,
            FontSize = style.FontSize,
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            Superscript = style.Superscript,
            Subscript = style.Subscript,
            FontColor = style.FontColor,
            FillColor = style.FillColor,
            FillPatternStyle = style.FillPatternStyle,
            FillPatternColor = style.FillPatternColor,
            BorderTop = FromCellBorder(style.BorderTop),
            BorderRight = FromCellBorder(style.BorderRight),
            BorderBottom = FromCellBorder(style.BorderBottom),
            BorderLeft = FromCellBorder(style.BorderLeft),
            NumberFormat = style.NumberFormat,
            HorizontalAlignment = style.HorizontalAlignment,
            VerticalAlignment = style.VerticalAlignment,
            WrapText = style.WrapText,
            ShrinkToFit = style.ShrinkToFit,
            DoubleUnderline = style.DoubleUnderline,
            IndentLevel = style.IndentLevel,
            TextRotation = style.TextRotation,
            Locked = style.Locked,
            Hidden = style.Hidden,
            NativeDifferentialAttributes = style.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = style.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = style.NativeDifferentialElementXmls
        })!;

        return new CellStyleDto
        {
            FontName = safeStyle.FontName,
            FontSize = safeStyle.FontSize,
            Bold = safeStyle.Bold,
            Italic = safeStyle.Italic,
            Underline = safeStyle.Underline,
            Strikethrough = safeStyle.Strikethrough,
            Superscript = safeStyle.Superscript,
            Subscript = safeStyle.Subscript,
            FontColor = safeStyle.FontColor,
            FillColor = safeStyle.FillColor,
            FillPatternStyle = safeStyle.FillPatternStyle,
            FillPatternColor = safeStyle.FillPatternColor,
            BorderTop = FromCellBorder(safeStyle.BorderTop),
            BorderRight = FromCellBorder(safeStyle.BorderRight),
            BorderBottom = FromCellBorder(safeStyle.BorderBottom),
            BorderLeft = FromCellBorder(safeStyle.BorderLeft),
            NumberFormat = safeStyle.NumberFormat,
            HorizontalAlignment = safeStyle.HorizontalAlignment,
            VerticalAlignment = safeStyle.VerticalAlignment,
            WrapText = safeStyle.WrapText,
            ShrinkToFit = safeStyle.ShrinkToFit,
            DoubleUnderline = safeStyle.DoubleUnderline,
            IndentLevel = safeStyle.IndentLevel,
            TextRotation = safeStyle.TextRotation,
            Locked = safeStyle.Locked,
            Hidden = safeStyle.Hidden,
            NativeDifferentialAttributes = safeStyle.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = safeStyle.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = safeStyle.NativeDifferentialElementXmls
        };
    }

    private static CellBorder ToCellBorder(CellBorderDto? border) =>
        border is null
            ? default
            : new CellBorder(NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None), border.Color);

    private static CellBorderDto FromCellBorder(CellBorder border) => new()
    {
        Style = NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None),
        Color = border.Color
    };
}
