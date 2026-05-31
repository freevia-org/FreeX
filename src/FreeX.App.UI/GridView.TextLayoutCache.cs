using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const int DefaultTextLayoutCacheLimit = 8192;
    private const int TextWidthLayoutCacheLimit = 32768;

    private readonly record struct DefaultTextLayoutKey(
        string Text,
        string CultureName,
        double FontSize,
        double PixelsPerDip);

    private readonly record struct TextWidthLayoutKey(
        string Text,
        string CultureName,
        CellTypefaceKey Typeface,
        double FontSize,
        double PixelsPerDip);

    private FormattedText GetDefaultFormattedText(string text, double fontSize, double pixelsPerDip)
    {
        var key = new DefaultTextLayoutKey(text, CultureInfo.CurrentCulture.Name, fontSize, pixelsPerDip);
        if (_defaultTextLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_defaultTextLayoutCache.Count >= DefaultTextLayoutCacheLimit)
            _defaultTextLayoutCache.Clear();

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            fontSize,
            TextBrush,
            pixelsPerDip);
        _defaultTextLayoutCache.Add(key, formatted);
        return formatted;
    }

    private static bool CanUseDefaultFormattedText(CellStyle? style, bool wrapText)
    {
        if (wrapText)
            return false;
        if (style is null)
            return true;

        var usesDefaultFontName = string.IsNullOrWhiteSpace(style.FontName) ||
            string.Equals(style.FontName, "Calibri", StringComparison.OrdinalIgnoreCase);
        var usesDefaultFontSize = style.FontSize <= 0 ||
            Math.Abs(style.FontSize - DefaultCellFontSizePoints) < 0.001;

        return usesDefaultFontName &&
            usesDefaultFontSize &&
            !style.Bold &&
            !style.Italic &&
            !style.Underline &&
            !style.DoubleUnderline &&
            !style.Strikethrough &&
            !style.Superscript &&
            !style.Subscript &&
            style.FontColor.IsBlack;
    }

    private double MeasureCellTextWidth(
        string text,
        CellTypefaceKey typefaceKey,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        var key = new TextWidthLayoutKey(text, CultureInfo.CurrentCulture.Name, typefaceKey, fontSize, pixelsPerDip);
        if (_textWidthLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_textWidthLayoutCache.Count >= TextWidthLayoutCacheLimit)
            _textWidthLayoutCache.Clear();

        var width = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            TextBrush,
            pixelsPerDip).Width;
        _textWidthLayoutCache.Add(key, width);
        return width;
    }
}
