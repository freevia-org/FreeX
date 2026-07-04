using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    // The WPF print path renders only black text on a white page background with optional
    // light-gray gridlines — it never renders cell fill colours or coloured fonts.
    // Black-and-white mode is therefore satisfied by construction on this path; there is
    // no blackAndWhite parameter here (unlike the Avalonia/Skia path which does render
    // fills and must suppress them when B&W is requested).
    private static void DrawPrintedGridCells(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ICollection<PdfLinkOverlay> linkOverlays,
        ICollection<PdfCellDestinationOverlay> cellDestinationOverlays,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyDictionary<(uint Row, uint Col), PdfLinkTarget> hyperlinkLookup,
        IReadOnlyDictionary<(uint Row, uint Col), CellAddress> cellDestinationLookup,
        bool printGridlines,
        WorksheetPrintErrorValue printErrorValue,
        double gridLeft,
        double gridTop)
    {
        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            var rowHeight = measurement.RowHeightAt(rowIndex);
            var y = gridTop + measurement.RowOffset(rowIndex);
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                var colWidth = measurement.ColumnWidthAt(colIndex);
                var x = gridLeft + measurement.ColumnOffset(colIndex);

                if (printGridlines)
                {
                    dc.DrawRectangle(null,
                        new Pen(Brushes.LightGray, 0.5),
                        new Rect(x, y, colWidth, rowHeight));
                }

                if (hyperlinkLookup.TryGetValue((row, col), out var link))
                {
                    linkOverlays.Add(new PdfLinkOverlay(
                        link.Target,
                        link.TargetKind,
                        x,
                        y,
                        colWidth,
                        rowHeight,
                        link.SourceAddress,
                        link.TargetAddress));
                }

                if (cellDestinationLookup.TryGetValue((row, col), out var destinationAddress))
                {
                    cellDestinationOverlays.Add(new PdfCellDestinationOverlay(
                        destinationAddress,
                        x,
                        y,
                        colWidth,
                        rowHeight));
                }

                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    string.IsNullOrEmpty(cell.DisplayText))
                {
                    continue;
                }

                var displayText = FormatPrintedCellText(cell.DisplayText, printErrorValue);
                if (string.IsNullOrEmpty(displayText))
                    continue;

                var textBrush = Brushes.Black;
                var textColor = Colors.Black;

                var ft = new FormattedText(
                    displayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    PrintedCellTypeface,
                    PrintFontSize,
                    textBrush,
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
                    textColor));
            }
        }
    }
}
