using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const int DefaultTextLayoutCacheLimit = 8192;
    private const int DefaultWrappedTextLayoutCacheLimit = 8192;
    private const int TextWidthLayoutCacheLimit = 32768;
    private const int ShrinkTextLayoutCacheLimit = 32768;

    private readonly record struct DefaultTextLayoutKey(
        string Text,
        string CultureName,
        double FontSize,
        double PixelsPerDip);

    private readonly record struct DefaultWrappedTextLayoutKey(
        string Text,
        string CultureName,
        double FontSize,
        double MaxTextWidth,
        TextAlignment TextAlignment,
        double PixelsPerDip);

    private readonly record struct TextWidthLayoutKey(
        string Text,
        string CultureName,
        CellTypefaceKey Typeface,
        double FontSize,
        double PixelsPerDip);

    private readonly record struct ShrinkTextLayoutKey(
        string Text,
        string CultureName,
        CellTypefaceKey Typeface,
        double RequestedFontSize,
        double AvailableWidth,
        double MinimumFontSize,
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

    private FormattedText GetDefaultWrappedFormattedText(
        string text,
        double fontSize,
        double maxTextWidth,
        TextAlignment textAlignment,
        double pixelsPerDip)
    {
        var key = new DefaultWrappedTextLayoutKey(
            text,
            CultureInfo.CurrentCulture.Name,
            fontSize,
            maxTextWidth,
            textAlignment,
            pixelsPerDip);
        if (_defaultWrappedTextLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_defaultWrappedTextLayoutCache.Count >= DefaultWrappedTextLayoutCacheLimit)
            _defaultWrappedTextLayoutCache.Clear();

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            fontSize,
            TextBrush,
            pixelsPerDip)
        {
            MaxTextWidth = maxTextWidth,
            TextAlignment = textAlignment
        };
        _defaultWrappedTextLayoutCache.Add(key, formatted);
        return formatted;
    }

    private bool CanUseDefaultFormattedText(CellStyle? style, bool wrapText)
    {
        if (wrapText)
            return false;

        return UsesDefaultTextLayoutStyle(style);
    }

    private bool CanUseDefaultWrappedFormattedText(CellStyle? style)
    {
        if (style?.WrapText != true)
            return false;

        return UsesDefaultTextLayoutStyle(style);
    }

    private bool UsesDefaultTextLayoutStyle(CellStyle? style)
    {
        if (style is null)
            return true;

        // The cache is keyed on style object reference equality. When the WorkbookTheme changes,
        // the WorkbookTheme property callback clears this cache so stale entries do not survive
        // a Theme Fonts switch.
        if (_defaultTextLayoutStyleCache.TryGetValue(style, out var cached))
            return cached;

        var result = UsesDefaultTextLayoutStyleCore(style, WorkbookTheme);
        _defaultTextLayoutStyleCache[style] = result;
        return result;
    }

    private static bool UsesDefaultTextLayoutStyleCore(CellStyle style, WorkbookTheme theme)
    {
        var (effectiveFontName, fontStretch) = ResolveEffectiveCellFontForDisplay(style, theme);
        var usesDefaultFontName = string.Equals(effectiveFontName, "Calibri", StringComparison.OrdinalIgnoreCase);
        var usesDefaultFontSize = style.FontSize <= 0 ||
            Math.Abs(style.FontSize - DefaultCellFontSizePoints) < 0.001;

        return usesDefaultFontName &&
            fontStretch == FontStretches.Normal &&
            usesDefaultFontSize &&
            !style.Bold &&
            !style.Italic &&
            !style.Underline &&
            !style.DoubleUnderline &&
            !style.Strikethrough &&
            !style.Superscript &&
            !style.Subscript &&
            // Must check the theme-RESOLVED color, not the baked style.FontColor: a cell whose
            // FontThemeColor happened to bake to black under the theme in effect at load/creation
            // time would otherwise stay eligible for this "assume default black text" fast path
            // forever, even after a theme swap re-resolves FontThemeColor to a non-black color --
            // silently painting it black instead of the correct themed color.
            style.ResolveFontColor(theme).IsBlack;
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

    private double ResolveCachedShrinkFontSize(
        string text,
        CellTypefaceKey typefaceKey,
        Typeface typeface,
        double requestedFontSize,
        double availableWidth,
        double minimumFontSize,
        double pixelsPerDip)
    {
        var key = new ShrinkTextLayoutKey(
            text,
            CultureInfo.CurrentCulture.Name,
            typefaceKey,
            requestedFontSize,
            availableWidth,
            minimumFontSize,
            pixelsPerDip);
        if (_shrinkTextLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_shrinkTextLayoutCache.Count >= ShrinkTextLayoutCacheLimit)
            _shrinkTextLayoutCache.Clear();

        var resolved = ResolveShrinkFontSize(
            requestedFontSize,
            availableWidth,
            size => MeasureCellTextWidth(text, typefaceKey, typeface, size, pixelsPerDip),
            minimumFontSize);
        _shrinkTextLayoutCache.Add(key, resolved);
        return resolved;
    }
}
