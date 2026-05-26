using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static void DrawPrintHeadings(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        double marginLeft,
        double marginTop,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        var headerBrush = new SolidColorBrush(Color.FromRgb(242, 242, 242));
        var headerPen = new Pen(Brushes.LightGray, 0.5);
        var typeface = new Typeface("Segoe UI");

        dc.DrawRectangle(headerBrush, headerPen,
            new Rect(marginLeft, marginTop, measurement.HeaderWidth, measurement.HeaderHeight));

        for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
        {
            var rect = new Rect(
                marginLeft + measurement.HeaderWidth + colIndex * measurement.ColumnWidth,
                marginTop,
                measurement.ColumnWidth,
                measurement.HeaderHeight);
            dc.DrawRectangle(headerBrush, headerPen, rect);
            DrawCenteredText(dc, textOverlays, CellAddress.NumberToColumnName(pageColumns[colIndex]), rect, typeface);
        }

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var rect = new Rect(
                marginLeft,
                marginTop + measurement.HeaderHeight + rowIndex * measurement.RowHeight,
                measurement.HeaderWidth,
                measurement.RowHeight);
            dc.DrawRectangle(headerBrush, headerPen, rect);
            DrawCenteredText(dc, textOverlays, pageRows[rowIndex].ToString(CultureInfo.InvariantCulture), rect, typeface);
        }
    }

    private static void DrawCenteredText(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Rect rect,
        Typeface typeface)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 4),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };

        var drawPoint = new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2);
        var textWidth = Math.Min(ft.WidthIncludingTrailingWhitespace, Math.Max(1, rect.Width - 4));
        var overlayX = rect.Left + Math.Max(2, (rect.Width - textWidth) / 2);
        dc.DrawText(ft, drawPoint);
        textOverlays.Add(new PdfTextOverlay(
            text,
            overlayX,
            drawPoint.Y,
            PrintFontSize,
            "Segoe UI",
            Bold: false,
            Italic: false,
            Colors.Black));
    }
}
