using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Freexcel.App.UI;

public partial class GridView
{
    private const int DefaultTextLayoutCacheLimit = 8192;

    private readonly record struct DefaultTextLayoutKey(
        string Text,
        string CultureName,
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
}
