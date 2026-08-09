using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Text;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly ITextMeasurer PrintTextMeasurer = new WpfPrintTextMeasurer();

    private sealed class WpfPrintTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(text))
                return TextSize.Empty;

            var family = new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily);
            var typeface = new Typeface(
                family,
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize > 0 ? fontSize : 9,
                Brushes.Black,
                1.0);
            return new TextSize(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
        }
    }
}
