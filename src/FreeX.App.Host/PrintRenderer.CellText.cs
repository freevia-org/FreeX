using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    // Excel's own default cell font/size (matches GridView.Rendering.cs's DefaultCellFontSizePoints /
    // CreateCellTypefaceKey fallback of "Calibri", 11pt) — used only when a cell has no style at all,
    // or its style leaves FontName/FontSize unset, so the print path falls back identically to the
    // interactive grid instead of a fixed print-only font.
    private const string DefaultPrintedCellFontName = "Calibri";
    private const double DefaultPrintedCellFontSizePoints = 11.0;

    /// <summary>
    /// Converts a point-based font size to the WPF device-independent-pixel em-size used by
    /// <see cref="FormattedText"/>, mirroring GridView.ToDisplayFontSize so a cell's printed font
    /// renders at the same relative size as it does on screen.
    /// </summary>
    private static double PointsToPrintedFontSizeDip(double points) => Math.Max(1.0, points * 96.0 / 72.0);

    /// <summary>
    /// Resolves the printed em-size (in DIPs) for a cell from its <see cref="CellStyle.FontSize"/>,
    /// falling back to Excel's default 11pt only when the style is absent or leaves the size unset.
    /// </summary>
    private static double ResolvePrintedCellFontSizeDip(CellStyle? style)
    {
        var points = style is not null && style.FontSize > 0 ? style.FontSize : DefaultPrintedCellFontSizePoints;
        return PointsToPrintedFontSizeDip(points);
    }

    /// <summary>
    /// Builds the printed <see cref="Typeface"/> from a cell's <see cref="CellStyle.FontName"/>,
    /// <see cref="CellStyle.Bold"/>, and <see cref="CellStyle.Italic"/>, falling back to Calibri
    /// non-bold/non-italic only when the style is absent or leaves the font name unset.
    /// </summary>
    private static Typeface ResolvePrintedCellTypeface(CellStyle? style)
    {
        var fontName = string.IsNullOrWhiteSpace(style?.FontName) ? DefaultPrintedCellFontName : style!.FontName;
        var fontStyle = style?.Italic == true ? FontStyles.Italic : FontStyles.Normal;
        var fontWeight = style?.Bold == true ? FontWeights.Bold : FontWeights.Normal;
        return new Typeface(new FontFamily(fontName), fontStyle, fontWeight, FontStretches.Normal);
    }

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
        => PagePrintTextPlanner.FormatPrintedCellText(displayText, printErrorValue);

    private static string BoundPrintedCellOverlayText(string text, double maxWidth, Typeface typeface, double fontSize) =>
        BoundPrintedSingleLineOverlayText(text, maxWidth, typeface, fontSize);

    private static string BoundPrintedSingleLineOverlayText(string text, double maxWidth, Typeface typeface, double fontSize = PrintFontSize)
    {
        const string ellipsis = "\u2026";
        var boundedWidth = Math.Max(1, maxWidth);
        var candidate = text.TrimEnd();
        if (FitsPrintedSingleLineVisibleWidth(candidate, boundedWidth, typeface, fontSize))
            return candidate;

        while (candidate.Length > 0 && !FitsPrintedSingleLineOverlayWidth(candidate + ellipsis, boundedWidth, typeface, fontSize))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static bool FitsPrintedSingleLineVisibleWidth(string text, double maxWidth, Typeface typeface, double fontSize = PrintFontSize) =>
        MeasurePrintedSingleLineText(text, typeface, fontSize).Width <= Math.Max(1, maxWidth);

    private static bool FitsPrintedSingleLineOverlayWidth(string text, double maxWidth, Typeface typeface, double fontSize = PrintFontSize) =>
        MeasurePrintedSingleLineText(text, typeface, fontSize).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static FormattedText MeasurePrintedSingleLineText(string text, Typeface typeface, double fontSize = PrintFontSize) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
}
