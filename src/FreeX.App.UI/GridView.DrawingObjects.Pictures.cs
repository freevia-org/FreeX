using System.Globalization;
using System.Windows;
using System.Windows.Media;

using FreeX.Core.Calc;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.UI;

public partial class GridView
{
    private static readonly Pen PictureBorderPen = CreateFrozenPen(MakeBrush(120, 120, 120), 1);
    private static readonly Pen PictureGridPen = CreateFrozenPen(MakeBrush(210, 210, 210), 0.75);
    private const int CroppedPictureBrushCacheLimit = 256;
    private readonly Dictionary<CroppedPictureBrushCacheKey, ImageBrush> _croppedPictureBrushCache = new();
    private ImageBrush? _worksheetBackgroundBrushCache;
    private WorksheetBackgroundBrushCacheKey _worksheetBackgroundBrushCacheKey;

    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var fill = Brushes.White;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        foreach (var picture in Pictures)
            RenderPicture(dc, metricLookups, picture, fill, pixelsPerDip, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
    }

    private void RenderPicture(
        DrawingContext dc,
        RenderMetricLookupCache metricLookups,
        PictureModel picture,
        Brush fill,
        double pixelsPerDip,
        double visibleRight,
        double visibleBottom,
        uint lastRenderableRow,
        uint lastRenderableColumn)
    {
        if (!ShouldDisplayAnchoredDrawingObject(picture.IsVisible, picture.Anchor, lastRenderableRow, lastRenderableColumn))
            return;
        if (!TryCreateAnchoredObjectRect(
                metricLookups,
                picture.Anchor,
                picture.Width,
                picture.Height,
                MinimumPictureObjectWidth,
                MinimumPictureObjectHeight,
                out var rect,
                picture.AnchorOffsetX,
                picture.AnchorOffsetY))
            return;

        var rotationDegrees = picture.RotationDegrees;
        var flipHorizontal = picture.FlipHorizontal;
        var flipVertical = picture.FlipVertical;
        if (TryResolveLiveObjectTransform(
                picture.Id,
                ObjectKind.Picture,
                rect,
                rotationDegrees,
                flipHorizontal,
                flipVertical,
                out var previewRect,
                out var previewRotationDegrees,
                out var previewFlipHorizontal,
                out var previewFlipVertical))
        {
            rect = previewRect;
            rotationDegrees = previewRotationDegrees;
            flipHorizontal = previewFlipHorizontal;
            flipVertical = previewFlipVertical;
        }

        if (!ShouldDisplayDrawingObjectRect(rect, rotationDegrees, visibleRight, visibleBottom))
            return;

        var transformDepth = PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect);

        if (picture.Kind == PictureKind.Image &&
            TryLoadPictureImage(picture, out var image) &&
            image is not null)
        {
            var crop = TryResolveLivePictureCrop(picture.Id, out var liveCrop)
                ? liveCrop
                : new PictureCropRatios(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);
            if (HasPictureCrop(crop))
            {
                var brush = GetCroppedPictureBrush(crop, image);
                dc.DrawRectangle(brush, null, rect);
            }
            else
            {
                dc.DrawImage(image, rect);
            }
            // R60-render-drawing-shapes-6-3: Excel draws no border on an inserted picture unless the
            // user explicitly applies one via Picture Format > Picture Border ("No Line" is the
            // default). PictureModel does not yet capture an authored <a:ln> outline to gate on, so
            // -- at minimum -- stop drawing the unconditional flat gray border every image picture
            // used to get regardless of its source formatting.
            PopDrawingObjectTransform(dc, transformDepth);
            return;
        }

        var rows = Math.Max(1, picture.SourceRowCount);
        var cols = Math.Max(1, picture.SourceColumnCount);
        var cellWidth = rect.Width / cols;
        var cellHeight = rect.Height / rows;
        // Built as a manual last-wins loop rather than .ToDictionary(...): PictureModel.Cells is a
        // plain List<PictureCellSnapshot> with no uniqueness constraint on (RowOffset, ColumnOffset),
        // and a hand-edited or adversarial .fxl file can legitimately contain duplicate offsets. A
        // straight ToDictionary throws ArgumentException on the second duplicate and crashes the
        // render; last-wins keeps the render resilient and picks the later (later-drawn) entry,
        // matching normal "last write wins" dictionary-assignment semantics. Mirrors the Avalonia
        // shell's MainWindow.cs picture-snapshot renderer (N52 fix) so both platforms behave the same
        // on the same adversarial file.
        var cellLookup = new Dictionary<(uint RowOffset, uint ColumnOffset), PictureCellSnapshot>();
        foreach (var cell in picture.Cells)
        {
            if (cell.RowOffset < rows && cell.ColumnOffset < cols)
                cellLookup[(cell.RowOffset, cell.ColumnOffset)] = cell;
        }

        dc.DrawRectangle(fill, PictureBorderPen, rect);

        for (uint row = 0; row < rows; row++)
        {
            for (uint col = 0; col < cols; col++)
            {
                if (!cellLookup.TryGetValue((row, col), out var cell) || cell.Style is not { } style)
                    continue;

                var cellRect = new Rect(
                    rect.Left + col * cellWidth,
                    rect.Top + row * cellHeight,
                    cellWidth,
                    cellHeight);
                DrawPictureCellStyle(dc, cellRect, style);
            }
        }

        for (uint r = 1; r < rows; r++)
        {
            var y = rect.Top + r * cellHeight;
            dc.DrawLine(PictureGridPen, new Point(rect.Left, y), new Point(rect.Right, y));
        }

        for (uint c = 1; c < cols; c++)
        {
            var x = rect.Left + c * cellWidth;
            dc.DrawLine(PictureGridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
        }

        foreach (var cell in cellLookup.Values)
        {
            var cellRect = new Rect(
                rect.Left + cell.ColumnOffset * cellWidth,
                rect.Top + cell.RowOffset * cellHeight,
                cellWidth,
                cellHeight);
            DrawPictureCellText(dc, cell, cellRect, pixelsPerDip);
            if (cell.Style is { } style && HasVisibleCellBorder(style))
                DrawPictureCellBorders(dc, cellRect, style);
        }

        PopDrawingObjectTransform(dc, transformDepth);
    }

    private void DrawPictureCellStyle(DrawingContext dc, Rect rect, CellStyle style)
    {
        Brush? fillBrush = style.ResolveFillColor(WorkbookTheme) is { } fillColor
            ? BrushForCellColor(fillColor, _brushCache)
            : null;
        if (fillBrush is not null)
            dc.DrawRectangle(fillBrush, null, rect);

        DrawFillPattern(dc, rect, style, WorkbookTheme, _brushCache, _fillPatternPenCache);
    }

    private void DrawPictureCellText(
        DrawingContext dc,
        PictureCellSnapshot cell,
        Rect cellRect,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(cell.Text))
            return;

        var style = cell.Style;
        var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
        // Match the live grid's reading-order resolution (GridView.Rendering.cs) so a snapshot taken
        // from -- or now rendered on -- a right-to-left sheet mirrors General alignment and text flow
        // direction the same way the live cells it was copied from do (R88-render-rtl-bidi-5-2).
        var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
            style?.ReadingOrder ?? CellReadingOrder.Context, IsSheetRightToLeft);
        var flowDirection = isEffectivelyRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var textRotation = style?.TextRotation ?? 0;
        var renderText = PrepareCellDisplayTextForRender(cell.Text, textRotation);
        var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
        var indentPx = (style?.IndentLevel ?? 0) * 8.0;
        Brush textBrush = TextBrush;

        if (style?.ShrinkToFit == true && style.WrapText != true)
        {
            var typefaceKey = CreateCellTypefaceKeyWithTheme(style);
            var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
            fontSize = ResolveCachedShrinkFontSize(
                renderText,
                typefaceKey,
                typeface,
                fontSize,
                Math.Max(1, cellRect.Width - 4 - indentPx),
                ToDisplayFontSize(6),
                pixelsPerDip);
        }

        ResolveSuperSubFontAdjustment(style, fontSize, out fontSize, out double pictureSuperSubBaselineOffsetPx);

        var typefaceForText = CreateCellTypefaceWithTheme(style, _typefaceCache);
        if (style?.ResolveFontColor(WorkbookTheme) is { } fontColor && !fontColor.IsBlack)
            textBrush = BrushForCellColor(fontColor, _brushCache);

        var text = new FormattedText(
            renderText,
            CultureInfo.CurrentCulture,
            flowDirection,
            typefaceForText,
            fontSize,
            textBrush,
            pixelsPerDip);
        if (BuildTextDecorations(style) is { } decorations)
            text.SetTextDecorations(decorations);

        if (style?.WrapText == true)
        {
            text.MaxTextWidth = Math.Max(1, cellRect.Width - 4);
            text.TextAlignment = ResolveWrapTextAlignment(hAlign, cell.IsNumericOrDate, isEffectivelyRightToLeft);
        }
        else
        {
            text.Trimming = TextTrimming.CharacterEllipsis;
            text.MaxTextWidth = Math.Max(1, cellRect.Width - 4 - indentPx);
        }

        var textLayout = CalculateCellTextRenderLayout(
            cellRect,
            text.Width,
            text.Height,
            hAlign,
            style?.VerticalAlignment,
            cell.IsNumericOrDate,
            indentPx,
            textRotation,
            isEffectivelyRightToLeft);
        var clipRect = new Rect(
            cellRect.Left + 2,
            cellRect.Top + 1,
            Math.Max(1, cellRect.Width - 4),
            Math.Max(1, cellRect.Height - 2));
        dc.PushClip(GetDrawingObjectClipGeometry(clipRect));
        DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache, pictureSuperSubBaselineOffsetPx);
        dc.Pop();
    }

    private void DrawPictureCellBorders(DrawingContext dc, Rect rect, CellStyle style)
    {
        var borderPixelsPerDip = GetBorderEffectivePixelsPerDip();
        DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), _brushCache, _borderPenCache, borderPixelsPerDip);
        DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache, borderPixelsPerDip);
        DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), _brushCache, _borderPenCache, borderPixelsPerDip);
        DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache, borderPixelsPerDip);
    }

    private static bool HasPictureCrop(PictureCropRatios crop) =>
        crop.Left > 0 ||
        crop.Top > 0 ||
        crop.Right > 0 ||
        crop.Bottom > 0;

    private ImageBrush GetCroppedPictureBrush(PictureCropRatios crop, ImageSource image)
    {
        var key = new CroppedPictureBrushCacheKey(
            image,
            crop.Left,
            crop.Top,
            crop.Right,
            crop.Bottom);
        if (_croppedPictureBrushCache.TryGetValue(key, out var cached))
            return cached;

        if (_croppedPictureBrushCache.Count >= CroppedPictureBrushCacheLimit)
            _croppedPictureBrushCache.Clear();

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

        _croppedPictureBrushCache.Add(key, brush);
        return brush;
    }

    private void RenderWorksheetBackground(DrawingContext dc)
    {
        if (WorksheetBackground == null || !TryLoadWorksheetBackgroundImage(WorksheetBackground, out var image) || image == null)
            return;

        var brush = GetWorksheetBackgroundBrush(WorksheetBackground, image);

        dc.DrawRectangle(
            brush,
            null,
            new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, GetLogicalViewportWidth() - ActualRowHeaderWidth), Math.Max(0, GetLogicalViewportHeight() - EffectiveColHeaderHeight)));
    }

    /// <summary>
    /// Computes the pixel distance scrolled off-screen above/left-of the first visible row/column,
    /// so the tiled background brush's origin can stay anchored to cell A1 (matching Excel) instead
    /// of the fixed viewport rect. Uses the sheet's default row/column size as an approximation for
    /// rows/columns that scrolled out of the metrics window; exact for sheets without custom sizing.
    /// </summary>
    private (double RowScrollOffset, double ColScrollOffset) GetWorksheetBackgroundScrollOffsets()
    {
        var viewport = Viewport;
        if (viewport == null)
            return (0, 0);

        double rowOffset = 0;
        if (viewport.RowMetrics.Count > 0)
        {
            var firstRow = viewport.RowMetrics[0].Row;
            if (firstRow > 1 && SheetDefaultRowHeight > 0)
                rowOffset = (firstRow - 1) * SheetDefaultRowHeight;
        }

        double colOffset = 0;
        if (viewport.ColMetrics.Count > 0)
        {
            var firstCol = viewport.ColMetrics[0].Col;
            if (firstCol > 1 && SheetDefaultColumnWidth > 0)
                colOffset = (firstCol - 1) * SheetDefaultColumnWidth;
        }

        return (rowOffset, colOffset);
    }

    private ImageBrush GetWorksheetBackgroundBrush(WorksheetBackgroundImage background, ImageSource image)
    {
        var (rowScrollOffset, colScrollOffset) = GetWorksheetBackgroundScrollOffsets();

        // Anchor the tile pattern to cell A1: the viewport origin is offset by the scrolled-off
        // pixels (mod the tile size) so tiles stay glued to the cell grid instead of the window.
        var tileOriginX = ActualRowHeaderWidth - Mod(colScrollOffset, image.Width);
        var tileOriginY = EffectiveColHeaderHeight - Mod(rowScrollOffset, image.Height);

        var key = new WorksheetBackgroundBrushCacheKey(
            background,
            image,
            tileOriginX,
            tileOriginY,
            image.Width,
            image.Height);
        if (_worksheetBackgroundBrushCache is { } cached && _worksheetBackgroundBrushCacheKey == key)
            return cached;

        var brush = new ImageBrush(image)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(tileOriginX, tileOriginY, image.Width, image.Height),
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        if (brush.CanFreeze)
            brush.Freeze();

        _worksheetBackgroundBrushCache = brush;
        _worksheetBackgroundBrushCacheKey = key;
        return brush;
    }

    private static double Mod(double value, double modulus)
    {
        if (modulus <= 0 || !double.IsFinite(value))
            return 0;

        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static bool TryLoadWorksheetBackgroundImage(WorksheetBackgroundImage background, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(background.ImageBytes, out image);

    private static bool TryLoadPictureImage(PictureModel picture, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(picture.ImageBytes, out image);

    private readonly record struct WorksheetBackgroundBrushCacheKey(
        WorksheetBackgroundImage Background,
        ImageSource Image,
        double TileOriginX,
        double TileOriginY,
        double ImageWidth,
        double ImageHeight);

    private readonly record struct CroppedPictureBrushCacheKey(
        ImageSource Image,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom);
}
