using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Free.Shared.Drawing;
using Free.Shared.Drawing.Wpf;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedTextBoxTypeface = new("Segoe UI");

    private static void DrawPrintedShapes(DrawingContext dc, ICollection<PdfTextOverlay> textOverlays, IReadOnlyList<PageShapeBlock> shapes, bool blackAndWhite)
    {
        if (shapes.Count == 0)
            return;
        foreach (var shape in shapes)
        {
            var bounds = ToRect(shape.Bounds);
            var bodyClip = ToRect(shape.BodyClip);
            if (shape.Geometry.Contours.Count == 0 || bodyClip.Width <= 0 || bodyClip.Height <= 0)
                continue;
            dc.PushClip(new RectangleGeometry(bodyClip));
            var appearance = shape.Appearance;
            var transform = new Matrix();
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;
            transform.ScaleAt(appearance.Transform.FlipHorizontal ? -1 : 1,
                appearance.Transform.FlipVertical ? -1 : 1, centerX, centerY);
            if (double.IsFinite(appearance.Transform.RotationDegrees))
                transform.RotateAt(appearance.Transform.RotationDegrees, centerX, centerY);
            dc.PushTransform(new MatrixTransform(transform));

            Brush? fill = appearance.Paint.HasFill && !blackAndWhite ? CreateFrozenBrush(PresentationRgb.FromCellColor(appearance.Paint.Fill)) : null;
            if (fill is not null && appearance.FillGradient is { } gradient)
            {
                var end = gradient.EndColor;
                var (startPoint, endPoint) = gradient.Direction switch
                {
                    DrawingShapeGradientDirection.Horizontal => (new Point(0, 0.5), new Point(1, 0.5)),
                    DrawingShapeGradientDirection.Vertical => (new Point(0.5, 0), new Point(0.5, 1)),
                    DrawingShapeGradientDirection.DiagonalUp => (new Point(0, 1), new Point(1, 0)),
                    _ => (new Point(0, 0), new Point(1, 1))
                };
                fill = new LinearGradientBrush(Color.FromRgb(appearance.Paint.Fill.R, appearance.Paint.Fill.G, appearance.Paint.Fill.B),
                    Color.FromRgb(end.R, end.G, end.B), startPoint, endPoint);
            }
            Pen? outline = null;
            if (appearance.Paint.HasOutline && appearance.Outline.HasOutline)
            {
                outline = new Pen(blackAndWhite ? Brushes.Black : CreateFrozenBrush(PresentationRgb.FromCellColor(appearance.Paint.Outline)), appearance.Outline.ThicknessDip);
                if (Enum.TryParse<DrawingMlPresetDash>(appearance.Outline.Dash.ToString(), out var dash) && DrawingMlPresetDashPattern.Get(dash) is { } pattern)
                    outline.DashStyle = new DashStyle(pattern, 0);
            }
            dc.DrawGeometry(fill, outline, WpfShapeGeometryAdapter.ToGeometry(shape.Geometry, FillRule.Nonzero));
            if (outline is not null)
                foreach (var arrowhead in shape.Arrowheads)
                    dc.DrawGeometry(outline.Brush, null, WpfShapeGeometryAdapter.ToGeometry(arrowhead, FillRule.Nonzero));
            dc.Pop();

            var leftInset = Math.Max(0, shape.TextInsets?.Left ?? 0);
            var rightInset = Math.Max(0, shape.TextInsets?.Right ?? 0);
            var topInset = Math.Max(0, shape.TextInsets?.Top ?? 0);
            var bottomInset = Math.Max(0, shape.TextInsets?.Bottom ?? 0);
            var innerWidth = Math.Max(0, bounds.Width - leftInset - rightInset);
            var innerHeight = Math.Max(0, bounds.Height - topInset - bottomInset);
            if (innerWidth > 0 && innerHeight > 0 && !string.IsNullOrEmpty(shape.Text))
            {
                // Flipping a shape mirrors its outline, but Excel keeps its text readable.
                var textTransform = new Matrix();
                if (double.IsFinite(appearance.Transform.RotationDegrees))
                    textTransform.RotateAt(appearance.Transform.RotationDegrees, centerX, centerY);
                dc.PushTransform(new MatrixTransform(textTransform));
                var font = shape.TextFont;
                var textColor = blackAndWhite ? Colors.Black : Color.FromRgb(font.Color.R, font.Color.G, font.Color.B);
                var textBrush = new SolidColorBrush(textColor);
                var typeface = new Typeface(new FontFamily(font.FontFamily), font.Italic ? FontStyles.Italic : FontStyles.Normal,
                    font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var unboundedText = new FormattedText(shape.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, font.FontSize, textBrush, 1.0);
                // WPF wraps at MaxTextWidth. For no-wrap text use the longest unwrapped line as
                // the layout width, then position that layout within the shape before clipping.
                var layoutWidth = shape.TextWrap ? innerWidth : Math.Max(innerWidth, unboundedText.WidthIncludingTrailingWhitespace + 0.01);
                var formatted = new FormattedText(shape.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, font.FontSize, textBrush, 1.0)
                {
                    MaxTextWidth = layoutWidth,
                    MaxTextHeight = innerHeight,
                    TextAlignment = shape.TextHorizontalAlignment switch
                    {
                        DrawingShapeTextHAlign.Center => TextAlignment.Center,
                        DrawingShapeTextHAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left
                    },
                    Trimming = TextTrimming.None
                };
                var left = bounds.Left + leftInset + (shape.TextHorizontalAlignment switch
                {
                    DrawingShapeTextHAlign.Center => (innerWidth - layoutWidth) / 2,
                    DrawingShapeTextHAlign.Right => innerWidth - layoutWidth,
                    _ => 0
                });
                var top = shape.TextVerticalAlignment switch
                {
                    DrawingShapeTextVAnchor.Middle => bounds.Top + topInset + Math.Max(0, (innerHeight - formatted.Height) / 2),
                    DrawingShapeTextVAnchor.Bottom => bounds.Top + topInset + Math.Max(0, innerHeight - formatted.Height),
                    _ => bounds.Top + topInset
                };
                dc.PushClip(new RectangleGeometry(bounds));
                dc.DrawText(formatted, new Point(left, top));
                dc.Pop();
                dc.Pop();
                // PDF's selectable-text overlay has no clip geometry. Only include single-line,
                // unrotated text fully contained by the body and shape to avoid hidden text leaks.
                if (appearance.Transform.RotationDegrees == 0 && bodyClip.Contains(bounds) &&
                    !shape.Text.Contains('\n') && !shape.Text.Contains('\r') &&
                    unboundedText.Height <= innerHeight && unboundedText.WidthIncludingTrailingWhitespace <= innerWidth)
                {
                    var x = shape.TextHorizontalAlignment switch
                    {
                        DrawingShapeTextHAlign.Center => bounds.Left + leftInset + (innerWidth - formatted.WidthIncludingTrailingWhitespace) / 2,
                        DrawingShapeTextHAlign.Right => bounds.Left + leftInset + innerWidth - formatted.WidthIncludingTrailingWhitespace,
                        _ => bounds.Left + leftInset
                    };
                    textOverlays.Add(new PdfTextOverlay(shape.Text, x, top, font.FontSize, font.FontFamily,
                        font.Bold, font.Italic, textColor));
                }
            }
            dc.Pop();
        }
    }

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
                AddPrintedChartTextOverlays(textOverlays, chart, workbookTheme, chartRect, viewport, pageCellLookup, sheet);
        }
        dc.Pop();
    }

    private static bool ShouldPrintChart(ChartModel chart, Rect pageGridRect)
    {
        if (!chart.IsVisible || !chart.PrintObject ||
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
