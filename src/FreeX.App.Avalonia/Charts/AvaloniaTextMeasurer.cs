using System.Globalization;

using Avalonia.Media;

using FreeX.App.Presentation.Text;

namespace FreeX.App.Avalonia.Charts;

/// <summary>
/// Avalonia-backed <see cref="ITextMeasurer"/> for the portable chart layout engine. Measures a run
/// of text with <see cref="FormattedText"/> so the engine can size axis tick labels, legend entries,
/// data labels, and titles using the same font stack the Avalonia shell paints with.
/// </summary>
public sealed class AvaloniaTextMeasurer : ITextMeasurer
{
    /// <summary>The fallback font family used when a request supplies no explicit family.</summary>
    public const string DefaultFontFamily = "Calibri";

    private static readonly IBrush MeasurementBrush = Brushes.Black;

    /// <inheritdoc />
    public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
            return TextSize.Empty;

        // FormattedText requires a strictly positive font size; clamp defensively so a zero or
        // negative request from styling data never throws.
        var emSize = fontSize > 0 ? fontSize : 1;

        var typeface = new Typeface(
            string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily,
            italic ? FontStyle.Italic : FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal);

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            emSize,
            MeasurementBrush);

        return new TextSize(formatted.Width, formatted.Height);
    }
}
