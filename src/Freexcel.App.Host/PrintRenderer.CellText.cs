using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedCellTypeface = new("Segoe UI");

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
    {
        if (!IsErrorDisplayText(displayText))
            return displayText;

        return printErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => "",
            WorksheetPrintErrorValue.Dash => "--",
            WorksheetPrintErrorValue.NotAvailable => "#N/A",
            _ => displayText
        };
    }

    private static bool IsErrorDisplayText(string text) =>
        text is "#DIV/0!" or "#VALUE!" or "#REF!" or "#NAME?" or "#NULL!" or "#N/A" or "#NUM!";

    private static string BoundPrintedCellOverlayText(string text, double maxWidth) =>
        BoundPrintedSingleLineOverlayText(text, maxWidth, PrintedCellTypeface);

    private static string BoundPrintedSingleLineOverlayText(string text, double maxWidth, Typeface typeface)
    {
        const string ellipsis = "\u2026";
        var boundedWidth = Math.Max(1, maxWidth);
        var candidate = text.TrimEnd();
        if (FitsPrintedSingleLineVisibleWidth(candidate, boundedWidth, typeface))
            return candidate;

        while (candidate.Length > 0 && !FitsPrintedSingleLineOverlayWidth(candidate + ellipsis, boundedWidth, typeface))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static bool FitsPrintedSingleLineVisibleWidth(string text, double maxWidth, Typeface typeface) =>
        MeasurePrintedSingleLineText(text, typeface).Width <= Math.Max(1, maxWidth);

    private static bool FitsPrintedSingleLineOverlayWidth(string text, double maxWidth, Typeface typeface) =>
        MeasurePrintedSingleLineText(text, typeface).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static FormattedText MeasurePrintedSingleLineText(string text, Typeface typeface) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            PrintFontSize,
            Brushes.Black,
            1.0);
}
