using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static StyleId? GetCachedStyleId(
        Workbook workbook,
        ref Dictionary<CellStyleDto, StyleId>? styleIdCache,
        CellStyleDto? dto)
    {
        if (dto is null)
            return null;

        styleIdCache ??= new Dictionary<CellStyleDto, StyleId>(CellStyleDtoComparer.Instance);
        if (!styleIdCache.TryGetValue(dto, out var styleId))
        {
            styleId = workbook.RegisterStyle(ToCellStyle(dto)!);
            styleIdCache[dto] = styleId;
        }

        return styleId;
    }

    private static List<StyleId>? LoadCellStyleTable(Workbook workbook, IReadOnlyList<CellStyleDto>? styles)
    {
        if (styles is null || styles.Count == 0)
            return null;

        var styleIds = new List<StyleId>(styles.Count);
        foreach (var style in styles)
            styleIds.Add(style is null ? StyleId.Default : workbook.RegisterStyle(ToCellStyle(style)!));

        return styleIds;
    }

    private static StyleId? ResolveCellStyleId(
        Workbook workbook,
        IReadOnlyList<StyleId>? cellStyleTable,
        ref Dictionary<CellStyleDto, StyleId>? styleIdCache,
        int? styleId,
        CellStyleDto? inlineStyle)
    {
        if (styleId is { } id)
        {
            if (id == StyleId.Default.Value)
                return StyleId.Default;

            if (cellStyleTable is not null && id > 0 && id < cellStyleTable.Count)
                return cellStyleTable[id];
        }

        return GetCachedStyleId(workbook, ref styleIdCache, inlineStyle);
    }

    // Internal (not private): reused by NativeJsonVisualDtoMapper for picture-cell snapshot styles (P26).
    internal static CellStyle? ToCellStyle(CellStyleDto? dto)
    {
        if (dto is null)
            return null;

        return new CellStyle
        {
            FontName = string.IsNullOrWhiteSpace(dto.FontName) ? CellStyle.Default.FontName : dto.FontName,
            FontSize = NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.FontSize, CellStyle.Default.FontSize),
            Charset = dto.Charset,
            FontFamily = dto.FontFamily,
            Bold = dto.Bold,
            Italic = dto.Italic,
            Underline = dto.Underline,
            Strikethrough = dto.Strikethrough,
            Superscript = dto.Superscript,
            Subscript = dto.Subscript,
            FontColor = dto.FontColor,
            FontThemeColor = NativeJsonColorMapper.ToThemeColorReference(dto.FontThemeColor),
            FillColor = dto.FillColor,
            FillThemeColor = NativeJsonColorMapper.ToThemeColorReference(dto.FillThemeColor),
            FillPatternStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.FillPatternStyle, CellFillPatternStyle.None),
            FillPatternColor = dto.FillPatternColor,
            FillPatternThemeColor = NativeJsonColorMapper.ToThemeColorReference(dto.FillPatternThemeColor),
            GradientFill = ToCellGradientFill(dto.GradientFill),
            BorderTop = ToCellBorder(dto.BorderTop),
            BorderRight = ToCellBorder(dto.BorderRight),
            BorderBottom = ToCellBorder(dto.BorderBottom),
            BorderLeft = ToCellBorder(dto.BorderLeft),
            BorderDiagonalDown = ToCellBorder(dto.BorderDiagonalDown),
            BorderDiagonalUp = ToCellBorder(dto.BorderDiagonalUp),
            NumberFormat = string.IsNullOrWhiteSpace(dto.NumberFormat) ? CellStyle.Default.NumberFormat : dto.NumberFormat,
            HorizontalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.HorizontalAlignment, HorizontalAlignment.General),
            VerticalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.VerticalAlignment, VerticalAlignment.Bottom),
            WrapText = dto.WrapText,
            ShrinkToFit = dto.ShrinkToFit,
            DoubleUnderline = dto.DoubleUnderline,
            IndentLevel = Math.Clamp(dto.IndentLevel, 0, 15),
            TextRotation = NativeJsonValueSanitizer.ValidTextRotationOrDefault(dto.TextRotation),
            ReadingOrder = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.ReadingOrder, CellReadingOrder.Context),
            Locked = dto.Locked,
            Hidden = dto.Hidden,
            DxfBold = dto.DxfBold,
            DxfItalic = dto.DxfItalic,
            DxfUnderline = dto.DxfUnderline,
            DxfStrikethrough = dto.DxfStrikethrough,
            DxfFontColor = dto.DxfFontColor,
            NativeDifferentialAttributes = dto.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = dto.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = dto.NativeDifferentialElementXmls,
            FontScheme = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.FontScheme, CellFontScheme.None),
        };
    }

    // Internal (not private): reused by NativeJsonVisualDtoMapper for picture-cell snapshot styles (P26).
    internal static CellStyleDto? FromCellStyle(CellStyle? style)
    {
        if (style is null)
            return null;

        var safeStyle = ToCellStyle(new CellStyleDto
        {
            FontName = style.FontName,
            FontSize = style.FontSize,
            FontScheme = style.FontScheme,
            Charset = style.Charset,
            FontFamily = style.FontFamily,
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            Superscript = style.Superscript,
            Subscript = style.Subscript,
            FontColor = style.FontColor,
            FontThemeColor = NativeJsonColorMapper.FromThemeColorReference(style.FontThemeColor),
            FillColor = style.FillColor,
            FillThemeColor = NativeJsonColorMapper.FromThemeColorReference(style.FillThemeColor),
            FillPatternStyle = style.FillPatternStyle,
            FillPatternColor = style.FillPatternColor,
            FillPatternThemeColor = NativeJsonColorMapper.FromThemeColorReference(style.FillPatternThemeColor),
            GradientFill = FromCellGradientFill(style.GradientFill),
            BorderTop = FromCellBorder(style.BorderTop),
            BorderRight = FromCellBorder(style.BorderRight),
            BorderBottom = FromCellBorder(style.BorderBottom),
            BorderLeft = FromCellBorder(style.BorderLeft),
            BorderDiagonalDown = style.BorderDiagonalDown.Style != BorderStyle.None ? FromCellBorder(style.BorderDiagonalDown) : null,
            BorderDiagonalUp = style.BorderDiagonalUp.Style != BorderStyle.None ? FromCellBorder(style.BorderDiagonalUp) : null,
            NumberFormat = style.NumberFormat,
            HorizontalAlignment = style.HorizontalAlignment,
            VerticalAlignment = style.VerticalAlignment,
            WrapText = style.WrapText,
            ShrinkToFit = style.ShrinkToFit,
            DoubleUnderline = style.DoubleUnderline,
            IndentLevel = style.IndentLevel,
            TextRotation = style.TextRotation,
            ReadingOrder = style.ReadingOrder,
            Locked = style.Locked,
            Hidden = style.Hidden,
            DxfBold = style.DxfBold,
            DxfItalic = style.DxfItalic,
            DxfUnderline = style.DxfUnderline,
            DxfStrikethrough = style.DxfStrikethrough,
            DxfFontColor = style.DxfFontColor,
            NativeDifferentialAttributes = style.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = style.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = style.NativeDifferentialElementXmls
        })!;

        return new CellStyleDto
        {
            FontName = safeStyle.FontName,
            FontSize = safeStyle.FontSize,
            FontScheme = safeStyle.FontScheme,
            Charset = safeStyle.Charset,
            FontFamily = safeStyle.FontFamily,
            Bold = safeStyle.Bold,
            Italic = safeStyle.Italic,
            Underline = safeStyle.Underline,
            Strikethrough = safeStyle.Strikethrough,
            Superscript = safeStyle.Superscript,
            Subscript = safeStyle.Subscript,
            FontColor = safeStyle.FontColor,
            FontThemeColor = NativeJsonColorMapper.FromThemeColorReference(safeStyle.FontThemeColor),
            FillColor = safeStyle.FillColor,
            FillThemeColor = NativeJsonColorMapper.FromThemeColorReference(safeStyle.FillThemeColor),
            FillPatternStyle = safeStyle.FillPatternStyle,
            FillPatternColor = safeStyle.FillPatternColor,
            FillPatternThemeColor = NativeJsonColorMapper.FromThemeColorReference(safeStyle.FillPatternThemeColor),
            GradientFill = FromCellGradientFill(safeStyle.GradientFill),
            BorderTop = FromCellBorder(safeStyle.BorderTop),
            BorderRight = FromCellBorder(safeStyle.BorderRight),
            BorderBottom = FromCellBorder(safeStyle.BorderBottom),
            BorderLeft = FromCellBorder(safeStyle.BorderLeft),
            BorderDiagonalDown = safeStyle.BorderDiagonalDown.Style != BorderStyle.None ? FromCellBorder(safeStyle.BorderDiagonalDown) : null,
            BorderDiagonalUp = safeStyle.BorderDiagonalUp.Style != BorderStyle.None ? FromCellBorder(safeStyle.BorderDiagonalUp) : null,
            NumberFormat = safeStyle.NumberFormat,
            HorizontalAlignment = safeStyle.HorizontalAlignment,
            VerticalAlignment = safeStyle.VerticalAlignment,
            WrapText = safeStyle.WrapText,
            ShrinkToFit = safeStyle.ShrinkToFit,
            DoubleUnderline = safeStyle.DoubleUnderline,
            IndentLevel = safeStyle.IndentLevel,
            TextRotation = safeStyle.TextRotation,
            ReadingOrder = safeStyle.ReadingOrder,
            Locked = safeStyle.Locked,
            Hidden = safeStyle.Hidden,
            DxfBold = safeStyle.DxfBold,
            DxfItalic = safeStyle.DxfItalic,
            DxfUnderline = safeStyle.DxfUnderline,
            DxfStrikethrough = safeStyle.DxfStrikethrough,
            DxfFontColor = safeStyle.DxfFontColor,
            NativeDifferentialAttributes = safeStyle.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = safeStyle.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = safeStyle.NativeDifferentialElementXmls
        };
    }

    private static CellBorder ToCellBorder(CellBorderDto? border) =>
        border is null
            ? default
            : new CellBorder(
                NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None),
                border.Color,
                NativeJsonColorMapper.ToThemeColorReference(border.ThemeColor));

    private static CellBorderDto FromCellBorder(CellBorder border) => new()
    {
        Style = NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None),
        Color = border.Color,
        ThemeColor = NativeJsonColorMapper.FromThemeColorReference(border.ThemeColor)
    };

    private static CellGradientFill? ToCellGradientFill(CellGradientFillDto? dto)
    {
        if (dto is null)
            return null;

        // System.Text.Json permits an explicit null even though the DTO initializes this list.
        // Treat a malformed/null collection (and null entries within it) as a degenerate gradient
        // instead of letting workbook open escape with ArgumentNullException/NullReferenceException.
        var stops = (dto.Stops ?? [])
            .OfType<CellGradientStopDto>()
            .Select(s => new CellGradientStop(s.Position, s.Color))
            .ToList();
        if (stops.Count < 2)
            return null; // degenerate

        return new CellGradientFill
        {
            Type   = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.Type, CellGradientFillType.Linear),
            Degree = dto.Degree,
            Left   = dto.Left,
            Right  = dto.Right,
            Top    = dto.Top,
            Bottom = dto.Bottom,
            Stops  = stops,
        };
    }

    private static CellGradientFillDto? FromCellGradientFill(CellGradientFill? gradient)
    {
        if (gradient is null || gradient.Stops.Count < 2)
            return null;

        return new CellGradientFillDto
        {
            Type   = gradient.Type,
            Degree = gradient.Degree,
            Left   = gradient.Left,
            Right  = gradient.Right,
            Top    = gradient.Top,
            Bottom = gradient.Bottom,
            Stops  = gradient.Stops
                .Select(s => new CellGradientStopDto { Position = s.Position, Color = s.Color })
                .ToList(),
        };
    }

    private sealed class CellStyleDtoComparer : IEqualityComparer<CellStyleDto>
    {
        public static readonly CellStyleDtoComparer Instance = new();

        public bool Equals(CellStyleDto? x, CellStyleDto? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return string.Equals(x.FontName, y.FontName, StringComparison.Ordinal)
                && x.FontSize == y.FontSize
                && x.FontScheme == y.FontScheme
                && x.Charset == y.Charset
                && x.FontFamily == y.FontFamily
                && x.Bold == y.Bold
                && x.Italic == y.Italic
                && x.Underline == y.Underline
                && x.Strikethrough == y.Strikethrough
                && x.Superscript == y.Superscript
                && x.Subscript == y.Subscript
                && x.FontColor == y.FontColor
                && ThemeColorRefEquals(x.FontThemeColor, y.FontThemeColor)
                && x.FillColor == y.FillColor
                && ThemeColorRefEquals(x.FillThemeColor, y.FillThemeColor)
                && x.FillPatternStyle == y.FillPatternStyle
                && x.FillPatternColor == y.FillPatternColor
                && ThemeColorRefEquals(x.FillPatternThemeColor, y.FillPatternThemeColor)
                && GradientFillDtoEquals(x.GradientFill, y.GradientFill)
                && BorderEquals(x.BorderTop, y.BorderTop)
                && BorderEquals(x.BorderRight, y.BorderRight)
                && BorderEquals(x.BorderBottom, y.BorderBottom)
                && BorderEquals(x.BorderLeft, y.BorderLeft)
                && BorderEquals(x.BorderDiagonalDown, y.BorderDiagonalDown)
                && BorderEquals(x.BorderDiagonalUp, y.BorderDiagonalUp)
                && string.Equals(x.NumberFormat, y.NumberFormat, StringComparison.Ordinal)
                && x.HorizontalAlignment == y.HorizontalAlignment
                && x.VerticalAlignment == y.VerticalAlignment
                && x.WrapText == y.WrapText
                && x.ShrinkToFit == y.ShrinkToFit
                && x.DoubleUnderline == y.DoubleUnderline
                && x.IndentLevel == y.IndentLevel
                && x.TextRotation == y.TextRotation
                && x.ReadingOrder == y.ReadingOrder
                && x.Locked == y.Locked
                && x.Hidden == y.Hidden
                && x.DxfBold == y.DxfBold
                && x.DxfItalic == y.DxfItalic
                && x.DxfUnderline == y.DxfUnderline
                && x.DxfStrikethrough == y.DxfStrikethrough
                && x.DxfFontColor == y.DxfFontColor
                && DictionaryEquals(x.NativeDifferentialAttributes, y.NativeDifferentialAttributes)
                && ListEquals(x.NativeDifferentialChildXmls, y.NativeDifferentialChildXmls)
                && DictionaryEquals(x.NativeDifferentialElementXmls, y.NativeDifferentialElementXmls);
        }

        public int GetHashCode(CellStyleDto obj)
        {
            var hash = new HashCode();
            hash.Add(obj.FontName, StringComparer.Ordinal);
            hash.Add(obj.FontSize);
            hash.Add(obj.FontScheme);
            hash.Add(obj.Charset);
            hash.Add(obj.FontFamily);
            hash.Add(obj.Bold);
            hash.Add(obj.Italic);
            hash.Add(obj.Underline);
            hash.Add(obj.Strikethrough);
            hash.Add(obj.Superscript);
            hash.Add(obj.Subscript);
            hash.Add(obj.FontColor);
            hash.Add(obj.FontThemeColor?.Slot);
            hash.Add(obj.FontThemeColor?.Tint);
            hash.Add(obj.FillColor);
            hash.Add(obj.FillThemeColor?.Slot);
            hash.Add(obj.FillThemeColor?.Tint);
            hash.Add(obj.FillPatternStyle);
            hash.Add(obj.FillPatternColor);
            hash.Add(obj.FillPatternThemeColor?.Slot);
            hash.Add(obj.FillPatternThemeColor?.Tint);
            hash.Add(GetGradientFillDtoHashCode(obj.GradientFill));
            AddBorderHash(ref hash, obj.BorderTop);
            AddBorderHash(ref hash, obj.BorderRight);
            AddBorderHash(ref hash, obj.BorderBottom);
            AddBorderHash(ref hash, obj.BorderLeft);
            AddBorderHash(ref hash, obj.BorderDiagonalDown);
            AddBorderHash(ref hash, obj.BorderDiagonalUp);
            hash.Add(obj.NumberFormat, StringComparer.Ordinal);
            hash.Add(obj.HorizontalAlignment);
            hash.Add(obj.VerticalAlignment);
            hash.Add(obj.WrapText);
            hash.Add(obj.ShrinkToFit);
            hash.Add(obj.DoubleUnderline);
            hash.Add(obj.IndentLevel);
            hash.Add(obj.TextRotation);
            hash.Add(obj.ReadingOrder);
            hash.Add(obj.Locked);
            hash.Add(obj.Hidden);
            hash.Add(obj.DxfBold);
            hash.Add(obj.DxfItalic);
            hash.Add(obj.DxfUnderline);
            hash.Add(obj.DxfStrikethrough);
            hash.Add(obj.DxfFontColor);
            hash.Add(GetDictionaryHashCode(obj.NativeDifferentialAttributes));
            hash.Add(GetListHashCode(obj.NativeDifferentialChildXmls));
            hash.Add(GetDictionaryHashCode(obj.NativeDifferentialElementXmls));
            return hash.ToHashCode();
        }

        private static bool GradientFillDtoEquals(CellGradientFillDto? x, CellGradientFillDto? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Type != y.Type || x.Degree != y.Degree ||
                x.Left != y.Left || x.Right != y.Right ||
                x.Top != y.Top || x.Bottom != y.Bottom)
                return false;
            var xStops = (x.Stops ?? []).OfType<CellGradientStopDto>().ToList();
            var yStops = (y.Stops ?? []).OfType<CellGradientStopDto>().ToList();
            if (xStops.Count != yStops.Count) return false;
            for (int i = 0; i < xStops.Count; i++)
            {
                if (xStops[i].Position != yStops[i].Position ||
                    xStops[i].Color != yStops[i].Color)
                    return false;
            }
            return true;
        }

        private static int GetGradientFillDtoHashCode(CellGradientFillDto? dto)
        {
            if (dto is null) return 0;
            var h = new HashCode();
            h.Add(dto.Type);
            h.Add(dto.Degree);
            foreach (var stop in (dto.Stops ?? []).OfType<CellGradientStopDto>())
            {
                h.Add(stop.Position);
                h.Add(stop.Color);
            }
            return h.ToHashCode();
        }

        private static bool ThemeColorRefEquals(ThemeColorReferenceDto? x, ThemeColorReferenceDto? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.Slot == y.Slot && x.Tint == y.Tint);

        private static bool BorderEquals(CellBorderDto? x, CellBorderDto? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.Style == y.Style && x.Color == y.Color
                && x.ThemeColor?.Slot == y.ThemeColor?.Slot && x.ThemeColor?.Tint == y.ThemeColor?.Tint);

        private static void AddBorderHash(ref HashCode hash, CellBorderDto? border)
        {
            if (border is null)
            {
                hash.Add(0);
                return;
            }

            hash.Add(border.Style);
            hash.Add(border.Color);
            hash.Add(border.ThemeColor?.Slot);
            hash.Add(border.ThemeColor?.Tint);
        }

        private static bool DictionaryEquals(IReadOnlyDictionary<string, string>? x, IReadOnlyDictionary<string, string>? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Count != y.Count)
                return false;

            foreach (var (key, value) in x)
            {
                if (!y.TryGetValue(key, out var otherValue) || value != otherValue)
                    return false;
            }

            return true;
        }

        private static bool ListEquals(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Count != y.Count)
                return false;

            for (var i = 0; i < x.Count; i++)
            {
                if (x[i] != y[i])
                    return false;
            }

            return true;
        }

        private static int GetDictionaryHashCode(IReadOnlyDictionary<string, string>? dictionary)
        {
            if (dictionary is null)
                return 0;

            var code = 0;
            foreach (var (key, value) in dictionary)
                code ^= HashCode.Combine(
                    key is null ? 0 : StringComparer.Ordinal.GetHashCode(key),
                    value is null ? 0 : StringComparer.Ordinal.GetHashCode(value));
            return code;
        }

        private static int GetListHashCode(IReadOnlyList<string>? list)
        {
            if (list is null)
                return 0;

            var hash = new HashCode();
            foreach (var item in list)
                hash.Add(item, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
