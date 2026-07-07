using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedCellTypeface = new("Segoe UI");

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
        => PagePrintTextPlanner.FormatPrintedCellText(displayText, printErrorValue);

    private static string BoundPrintedCellOverlayText(string text, double maxWidth) =>
        BoundPrintedSingleLineOverlayText(text, maxWidth, PrintedCellTypeface, PrintFontSize);

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
