using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// Renders a worksheet as a WPF <see cref="FixedDocument"/> for printing or XPS export.
/// </summary>
public static class PrintRenderer
{
    /// <summary>
    /// Renders the used range of <paramref name="sheetId"/> onto FixedDocument pages (A4 portrait).
    /// Returns an empty document if the sheet has no data.
    /// </summary>
    public static FixedDocument RenderWorksheet(
        Workbook workbook,
        SheetId sheetId,
        IViewportService viewportService,
        double pageWidthInches  = 8.27,   // A4 portrait
        double pageHeightInches = 11.69)
    {
        const double dpi    = 96.0;
        double pageW  = pageWidthInches  * dpi;
        double pageH  = pageHeightInches * dpi;
        double margin = 0.5 * dpi;  // 0.5-inch margin on each side

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);

        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return doc;

        // Use the sheet's built-in used-range calculation
        var usedRange = sheet.GetUsedRange();
        if (usedRange == null) return doc;  // empty sheet → 0 pages

        uint maxRow = usedRange.Value.End.Row;
        uint maxCol = usedRange.Value.End.Col;

        // Fetch all cells at once via ViewportService
        double printableW = pageW - 2 * margin;
        double printableH = pageH - 2 * margin;

        var viewport = viewportService.GetViewport(workbook, sheetId,
            new ViewportRequest(
                TopRow:          1,
                LeftCol:         1,
                AvailableHeight: (double)maxRow * 9999,   // large enough to get all rows
                AvailableWidth:  (double)maxCol * 9999)); // large enough to get all cols

        // Build fast lookup dictionaries
        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));

        // Layout constants — distribute columns evenly across printable width
        double colWidth  = Math.Max(40.0, printableW / Math.Max(1, maxCol));
        double rowHeight = 20.0;

        uint rowsPerPage = (uint)Math.Floor(printableH / rowHeight);
        if (rowsPerPage < 1) rowsPerPage = 1;

        uint totalPages = (uint)Math.Ceiling((double)maxRow / rowsPerPage);

        for (uint page = 0; page < totalPages; page++)
        {
            uint startRow = page * rowsPerPage + 1;
            uint endRow   = Math.Min(startRow + rowsPerPage - 1, maxRow);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // White page background
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

                // Optional thin outer border around the print area
                dc.DrawRectangle(null, new Pen(Brushes.Black, 0.5),
                    new Rect(margin, margin, colWidth * maxCol, rowHeight * (endRow - startRow + 1)));

                for (uint r = startRow; r <= endRow; r++)
                {
                    for (uint c = 1; c <= maxCol; c++)
                    {
                        double x = margin + (c - 1) * colWidth;
                        double y = margin + (r - startRow) * rowHeight;

                        // Cell border
                        dc.DrawRectangle(null,
                            new Pen(Brushes.LightGray, 0.5),
                            new Rect(x, y, colWidth, rowHeight));

                        // Cell text
                        if (cellLookup.TryGetValue((r, c), out var cell) &&
                            !string.IsNullOrEmpty(cell.DisplayText))
                        {
                            var typeface = new Typeface("Segoe UI");
                            var ft = new FormattedText(
                                cell.DisplayText,
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                9.0,
                                Brushes.Black,
                                1.0);  // pixelsPerDip — safe default; no Window reference needed here
                            ft.MaxTextWidth  = Math.Max(1, colWidth  - 4);
                            ft.MaxLineCount  = 1;
                            ft.Trimming      = TextTrimming.CharacterEllipsis;

                            dc.DrawText(ft, new Point(x + 2, y + (rowHeight - ft.Height) / 2));
                        }
                    }
                }
            }

            // Wrap the DrawingVisual in a VisualHost so it can live inside FixedPage
            var container = new VisualHost { Visual = visual };
            var fixedPage = new FixedPage { Width = pageW, Height = pageH };
            fixedPage.Children.Add(container);
            FixedPage.SetLeft(container, 0);
            FixedPage.SetTop(container, 0);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            doc.Pages.Add(pageContent);
        }

        return doc;
    }
}

/// <summary>
/// Minimal UIElement wrapper that hosts an arbitrary <see cref="DrawingVisual"/>
/// inside a <see cref="FixedPage"/>.
/// </summary>
internal sealed class VisualHost : UIElement
{
    public Visual? Visual { get; init; }

    protected override int VisualChildrenCount => Visual != null ? 1 : 0;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0 || Visual == null)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Visual;
    }

    protected override void OnRender(DrawingContext drawingContext) { }
}
