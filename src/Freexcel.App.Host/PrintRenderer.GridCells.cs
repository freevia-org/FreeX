using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static void DrawPrintedGridCells(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        bool printGridlines,
        WorksheetPrintErrorValue printErrorValue,
        double gridLeft,
        double gridTop)
    {
        var rowHeight = measurement.RowHeight;
        var colWidth = measurement.ColumnWidth;
        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                double x = gridLeft + colIndex * colWidth;
                double y = gridTop + rowIndex * rowHeight;

                if (printGridlines)
                {
                    dc.DrawRectangle(null,
                        new Pen(Brushes.LightGray, 0.5),
                        new Rect(x, y, colWidth, rowHeight));
                }

                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    string.IsNullOrEmpty(cell.DisplayText))
                {
                    continue;
                }

                var displayText = FormatPrintedCellText(cell.DisplayText, printErrorValue);
                if (string.IsNullOrEmpty(displayText))
                    continue;

                var ft = new FormattedText(
                    displayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    PrintedCellTypeface,
                    PrintFontSize,
                    Brushes.Black,
                    1.0)
                {
                    MaxTextWidth = Math.Max(1, colWidth - 4),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };

                var textPoint = new Point(x + 2, y + (rowHeight - ft.Height) / 2);
                dc.DrawText(ft, textPoint);
                var overlayText = BoundPrintedCellOverlayText(displayText, ft.MaxTextWidth);
                textOverlays.Add(new PdfTextOverlay(
                    overlayText,
                    textPoint.X,
                    textPoint.Y,
                    PrintFontSize,
                    PrintedCellTypeface.FontFamily.Source,
                    Bold: false,
                    Italic: false,
                    Colors.Black));
            }
        }
    }
}
