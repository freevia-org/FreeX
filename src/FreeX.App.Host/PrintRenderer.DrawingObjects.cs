using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedTextBoxTypeface = new("Segoe UI");

    private static void DrawPrintedCharts(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ViewportModel viewport,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        WorkbookTheme workbookTheme,
        IReadOnlyList<uint> bodyRows,
        IReadOnlyList<uint> bodyColumns,
        int rowPlanTitleCount,
        int columnPlanTitleCount,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement)
    {
        if (charts.Count == 0 || bodyRows.Count == 0 || bodyColumns.Count == 0)
            return;

        var bodyGridLeft = gridLeft + measurement.ColumnOffset(columnPlanTitleCount);
        var bodyGridTop = gridTop + measurement.RowOffset(rowPlanTitleCount);
        var bodyGridRect = new Rect(
            bodyGridLeft,
            bodyGridTop,
            measurement.ColumnOffset(columnPlanTitleCount + bodyColumns.Count) - measurement.ColumnOffset(columnPlanTitleCount),
            measurement.RowOffset(rowPlanTitleCount + bodyRows.Count) - measurement.RowOffset(rowPlanTitleCount));

        // chart.Left/chart.Top/chart.Width/chart.Height are absolute pixel offsets/extents from the
        // sheet's real (non-uniform, hidden-row/column-skipping) origin in XlsxDrawingAnchorApplier's
        // width-in-chars*8 convention — see ChartAnchorGeometry. That is a DIFFERENT pixel-per-character
        // convention than the print grid's own column/row measurement (measurement.ColumnOffset, built from
        // ColumnWidthPixelMapper's width*7+5 convention), so chart.Left/pageGridLeft (both *8-space) must
        // never be summed directly with bodyGridLeft/measurement (7x+5-space), and chart.Width/chart.Height
        // must never be used unconverted alongside a grid-space position either. ShouldPrintChart's
        // intersection test stays in the anchor's own *8 space (pageGridRect below), but the chart's final
        // on-page rect is computed by first converting its anchor position AND extent into the grid's own
        // pixel space via ChartAnchorGeometry.ConvertColumnOffsetToGridSpace/ConvertRowOffsetToGridSpace and
        // ConvertColumnExtentToGridSpace/ConvertRowExtentToGridSpace, then translating within that single,
        // consistent space.
        var firstBodyColumn = bodyColumns[0];
        var firstBodyRow = bodyRows[0];
        var pageGridLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, firstBodyColumn - 1);
        var pageGridTop = ChartAnchorGeometry.SumRowPixels(sheet, 1, firstBodyRow - 1);
        var pageGridRect = new Rect(
            pageGridLeft,
            pageGridTop,
            bodyGridRect.Width,
            bodyGridRect.Height);
        var pageGridLeftInGridSpace = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, pageGridLeft);
        var pageGridTopInGridSpace = ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet, pageGridTop);

        dc.PushClip(new RectangleGeometry(bodyGridRect));
        foreach (var chart in charts)
        {
            if (!ShouldPrintChart(chart, pageGridRect))
                continue;

            var image = ChartRenderer.Render(chart, viewport, workbookTheme);
            if (image is null)
                continue;

            var chartGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, chart.Left);
            var chartGridTop = ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet, chart.Top);
            var chartGridWidth = ChartAnchorGeometry.ConvertColumnExtentToGridSpace(sheet, chart.Left, chart.Width);
            var chartGridHeight = ChartAnchorGeometry.ConvertRowExtentToGridSpace(sheet, chart.Top, chart.Height);
            var chartRect = new Rect(
                bodyGridLeft + chartGridLeft - pageGridLeftInGridSpace,
                bodyGridTop + chartGridTop - pageGridTopInGridSpace,
                chartGridWidth,
                chartGridHeight);
            dc.DrawImage(image, chartRect);
            if (bodyGridRect.Contains(chartRect))
                AddPrintedChartTextOverlays(textOverlays, chart, workbookTheme, chartRect, viewport, pageCellLookup);
        }
        dc.Pop();
    }

    private static bool ShouldPrintChart(ChartModel chart, Rect pageGridRect)
    {
        if (!chart.IsVisible ||
            !double.IsFinite(chart.Left) ||
            !double.IsFinite(chart.Top) ||
            !double.IsFinite(chart.Width) ||
            !double.IsFinite(chart.Height) ||
            chart.Width <= 0 ||
            chart.Height <= 0)
        {
            return false;
        }

        var chartRect = new Rect(chart.Left, chart.Top, chart.Width, chart.Height);
        return chartRect.IntersectsWith(pageGridRect);
    }

    /// <summary>
    /// R92-consumer-wiring-sweep-1: sheet pictures (Insert &gt; Pictures, or a raster non-linked Paste
    /// Special &gt; Picture) were never drawn by the print/XPS/Print-Preview renderer at all -- they
    /// rendered fine on screen (<c>GridView.RenderPicture</c>) but were simply absent from every
    /// printed page. Mirrors that screen renderer's crop handling (an <see cref="ImageBrush"/> with a
    /// relative <see cref="Viewbox"/> when cropped, a plain <see cref="DrawingContext.DrawImage"/>
    /// otherwise) so a cropped picture prints the same visible region it shows on screen.
    /// </summary>
    private static void DrawPrintedPictures(DrawingContext dc, IReadOnlyList<PagePictureBlock> pictures)
    {
        if (pictures.Count == 0)
            return;

        foreach (var picture in pictures)
        {
            if (!WpfBitmapImageLoader.TryLoad(picture.ImageBytes, out var image) || image is null)
                continue;

            var rect = ToRect(picture.Bounds);
            // Note: picture.Crop is FreeX.App.Presentation.DrawingInteraction.PictureCropRatios, kept
            // unnamed here (destructured via `var`) because FreeX.App.UI (already `using`-imported
            // above for WpfBitmapImageLoader/ChartRenderer) declares its own same-shaped
            // PictureCropRatios, and naming the type directly would be an ambiguous reference.
            var crop = picture.Crop;
            if (crop.Left > 0 || crop.Top > 0 || crop.Right > 0 || crop.Bottom > 0)
            {
                var brush = new ImageBrush(image)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new Rect(
                        crop.Left,
                        crop.Top,
                        Math.Max(0.01, 1 - crop.Left - crop.Right),
                        Math.Max(0.01, 1 - crop.Top - crop.Bottom))
                };
                if (brush.CanFreeze)
                    brush.Freeze();
                dc.DrawRectangle(brush, null, rect);
            }
            else
            {
                dc.DrawImage(image, rect);
            }
        }
    }

    private static void DrawPrintedTextBoxes(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyList<PageTextBoxBlock> textBoxes)
    {
        if (textBoxes.Count == 0)
            return;

        foreach (var textBox in textBoxes)
        {
            var rect = ToRect(textBox.Bounds);
            var fillBrush = textBox.Fill is { } fill ? CreateFrozenBrush(fill, textBox.FillAlpha) : null;
            // R91-commands-insert-object-5-1: Outline is null when the text box's line is
            // explicitly suppressed (TextBoxModel.OutlineHasNoFill) -- print no border rather than
            // always forcing one.
            Pen? outlinePen = null;
            if (textBox.Outline is { } outlineColor)
            {
                outlinePen = new Pen(CreateFrozenBrush(outlineColor), textBox.OutlineThickness);
                outlinePen.Freeze();
            }
            dc.DrawRectangle(fillBrush, outlinePen, rect);

            if (string.IsNullOrEmpty(textBox.Text))
                continue;

            var textRect = ToRect(textBox.TextBounds);
            DrawPrintedTextBoxText(dc, textBox.Text, textRect);
            AddPrintedTextBoxOverlays(textOverlays, textBox.Text, textRect);
        }
    }

    private static Rect ToRect(LayoutRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static void DrawPrintedTextBoxText(DrawingContext dc, string text, Rect textRect)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedTextBoxTypeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = textRect.Width,
            MaxTextHeight = textRect.Height,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.PushClip(new RectangleGeometry(textRect));
        dc.DrawText(formattedText, textRect.TopLeft);
        dc.Pop();
    }

    private static void AddPrintedTextBoxOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Rect textRect)
    {
        var lineHeight = MeasurePrintedTextBoxText("Ag").Height;
        var maxLines = Math.Max(1, (int)Math.Floor(textRect.Height / Math.Max(1, lineHeight)));
        var lineIndex = 0;
        foreach (var line in MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
                     text,
                     textRect.Width,
                     MeasurePrintedTextBoxWidth,
                     maxLines))
        {
            textOverlays.Add(new PdfTextOverlay(
                line,
                textRect.Left,
                textRect.Top + lineHeight * lineIndex,
                PrintFontSize,
                PrintedTextBoxTypeface.FontFamily.Source,
                Bold: false,
                Italic: false,
                Colors.Black));
            lineIndex++;
        }
    }

    private static double MeasurePrintedTextBoxWidth(string text) =>
        MeasurePrintedTextBoxText(text).WidthIncludingTrailingWhitespace;

    private static FormattedText MeasurePrintedTextBoxText(string text) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedTextBoxTypeface,
            PrintFontSize,
            Brushes.Black,
            1.0);

    private static SolidColorBrush CreateFrozenBrush(PresentationRgb color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
