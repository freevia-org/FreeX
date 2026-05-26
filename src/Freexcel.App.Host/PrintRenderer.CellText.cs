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

    private static string BoundPrintedCellOverlayText(string text, double maxWidth)
    {
        const string ellipsis = "\u2026";
        var boundedWidth = Math.Max(1, maxWidth);
        var candidate = text.TrimEnd();
        if (FitsPrintedCellVisibleWidth(candidate, boundedWidth))
            return candidate;

        while (candidate.Length > 0 && !FitsPrintedCellOverlayWidth(candidate + ellipsis, boundedWidth))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static bool FitsPrintedCellVisibleWidth(string text, double maxWidth) =>
        MeasurePrintedCellText(text).Width <= Math.Max(1, maxWidth);

    private static bool FitsPrintedCellOverlayWidth(string text, double maxWidth) =>
        MeasurePrintedCellText(text).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static FormattedText MeasurePrintedCellText(string text) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedCellTypeface,
            PrintFontSize,
            Brushes.Black,
            1.0);
}
