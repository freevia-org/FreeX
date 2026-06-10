using System.Globalization;
using System.Windows;
using System.Windows.Media;

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
        if (!picture.IsVisible) return;
        if (!CanAnchoredObjectReachDrawingViewport(picture.Anchor, lastRenderableRow, lastRenderableColumn))
            return;
        if (!TryCreateAnchoredObjectRect(
                metricLookups,
                picture.Anchor,
                picture.Width,
                picture.Height,
                MinimumPictureObjectWidth,
                MinimumPictureObjectHeight,
                out var rect))
            return;

        var rotationDegrees = picture.RotationDegrees;
        if (TryResolveLiveObjectTransform(picture.Id, ObjectKind.Picture, rect, rotationDegrees, out var previewRect, out var previewRotationDegrees))
        {
            rect = previewRect;
            rotationDegrees = previewRotationDegrees;
        }

        if (NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom) &&
            !IntersectsDrawingViewport(rect, rotationDegrees, visibleRight, visibleBottom))
            return;

        if (Math.Abs(rotationDegrees) > 0.0001)
            dc.PushTransform(new RotateTransform(
                rotationDegrees,
                rect.Left + rect.Width / 2,
                rect.Top + rect.Height / 2));

        if (picture.Kind == PictureKind.Image &&
            TryLoadPictureImage(picture, out var image) &&
            image is not null)
        {
            if (HasPictureCrop(picture))
            {
                var brush = GetCroppedPictureBrush(picture, image);
                dc.DrawRectangle(brush, null, rect);
            }
            else
            {
                dc.DrawImage(image, rect);
            }
            dc.DrawRectangle(null, PictureBorderPen, rect);
            if (Math.Abs(rotationDegrees) > 0.0001)
                dc.Pop();
            return;
        }

        var rows = Math.Max(1, picture.SourceRowCount);
        var cols = Math.Max(1, picture.SourceColumnCount);
        var cellWidth = rect.Width / cols;
        var cellHeight = rect.Height / rows;
        var cellLookup = picture.Cells
            .Where(cell => cell.RowOffset < rows && cell.ColumnOffset < cols)
            .ToDictionary(cell => (cell.RowOffset, cell.ColumnOffset));

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

        if (Math.Abs(rotationDegrees) > 0.0001)
            dc.Pop();
    }

    private void DrawPictureCellStyle(DrawingContext dc, Rect rect, CellStyle style)
    {
        Brush? fillBrush = style.FillColor is { } fillColor
            ? BrushForCellColor(fillColor, _brushCache)
            : null;
        if (fillBrush is not null)
            dc.DrawRectangle(fillBrush, null, rect);

        DrawFillPattern(dc, rect, style, _brushCache, _fillPatternPenCache);
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
        var textRotation = style?.TextRotation ?? 0;
        var renderText = PrepareCellDisplayTextForRender(cell.Text, textRotation);
        var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
        var indentPx = (style?.IndentLevel ?? 0) * 8.0;
        Brush textBrush = TextBrush;

        if (style?.ShrinkToFit == true && style.WrapText != true)
        {
            var typefaceKey = CreateCellTypefaceKey(style);
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

        var typefaceForText = CreateCellTypeface(style);
        if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
            textBrush = BrushForCellColor(fontColor, _brushCache);

        var text = new FormattedText(
            renderText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typefaceForText,
            fontSize,
            textBrush,
            pixelsPerDip);
        if (BuildTextDecorations(style) is { } decorations)
            text.SetTextDecorations(decorations);

        if (style?.WrapText == true)
        {
            text.MaxTextWidth = Math.Max(1, cellRect.Width - 4);
            text.TextAlignment = hAlign switch
            {
                CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,
                CellHAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };
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
            textRotation);
        var clipRect = new Rect(
            cellRect.Left + 2,
            cellRect.Top + 1,
            Math.Max(1, cellRect.Width - 4),
            Math.Max(1, cellRect.Height - 2));
        dc.PushClip(GetDrawingObjectClipGeometry(clipRect));
        DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache);
        dc.Pop();
    }

    private void DrawPictureCellBorders(DrawingContext dc, Rect rect, CellStyle style)
    {
        DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), _brushCache, _borderPenCache);
        DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
        DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), _brushCache, _borderPenCache);
        DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private ImageBrush GetCroppedPictureBrush(PictureModel picture, ImageSource image)
    {
        var key = new CroppedPictureBrushCacheKey(
            image,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom);
        if (_croppedPictureBrushCache.TryGetValue(key, out var cached))
            return cached;

        if (_croppedPictureBrushCache.Count >= CroppedPictureBrushCacheLimit)
            _croppedPictureBrushCache.Clear();

        var brush = new ImageBrush(image)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(
                picture.CropLeft,
                picture.CropTop,
                Math.Max(0.01, 1 - picture.CropLeft - picture.CropRight),
                Math.Max(0.01, 1 - picture.CropTop - picture.CropBottom))
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
            new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, ActualWidth - ActualRowHeaderWidth), Math.Max(0, ActualHeight - EffectiveColHeaderHeight)));
    }

    private ImageBrush GetWorksheetBackgroundBrush(WorksheetBackgroundImage background, ImageSource image)
    {
        var key = new WorksheetBackgroundBrushCacheKey(
            background,
            image,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            image.Width,
            image.Height);
        if (_worksheetBackgroundBrushCache is { } cached && _worksheetBackgroundBrushCacheKey == key)
            return cached;

        var brush = new ImageBrush(image)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, image.Width, image.Height),
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

    private static bool TryLoadWorksheetBackgroundImage(WorksheetBackgroundImage background, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(background.ImageBytes, out image);

    private static bool TryLoadPictureImage(PictureModel picture, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(picture.ImageBytes, out image);

    private readonly record struct WorksheetBackgroundBrushCacheKey(
        WorksheetBackgroundImage Background,
        ImageSource Image,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        double ImageWidth,
        double ImageHeight);

    private readonly record struct CroppedPictureBrushCacheKey(
        ImageSource Image,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom);
}
