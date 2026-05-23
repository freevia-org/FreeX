using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    // Floating drawing objects, pictures, charts, and worksheet background rendering.

    private static readonly Brush ObjectPlaceholderFill = MakeBrushAlpha(48, 255, 255, 255);
    private static readonly Brush ObjectPlaceholderTextBrush = MakeBrush(89, 89, 89);
    private static readonly Pen ObjectPlaceholderPen = CreateFrozenPen(MakeBrush(120, 120, 120), 1);

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        foreach (var chart in Charts)
        {
            if (!chart.IsVisible) continue;
            var img = ChartRenderer.Render(chart, Viewport, WorkbookTheme);
            if (img == null) continue;
            var rect = new Rect(
                chart.Left + ActualRowHeaderWidth, chart.Top + EffectiveColHeaderHeight,
                chart.Width, chart.Height);
            dc.DrawImage(img, rect);
        }
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        foreach (var textBox in TextBoxes)
        {
            if (!textBox.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == textBox.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == textBox.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(24, textBox.Width),
                Math.Max(18, textBox.Height));
            var rotationPushed = PushRotation(dc, textBox.RotationDegrees, rect);
            var colors = ResolveTextBoxColors(textBox, WorkbookTheme);
            DrawTextBoxThemeEffect(dc, rect, WorkbookTheme);
            var fillBrush = MakeBrushAlpha(242, colors.Fill.R, colors.Fill.G, colors.Fill.B);
            var borderPen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1);
            borderPen.Freeze();
            dc.DrawRectangle(fillBrush, borderPen, rect);

            var text = new FormattedText(
                textBox.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                12,
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, rect.Width - 8),
                MaxTextHeight = Math.Max(1, rect.Height - 8)
            };

            dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, rect.Width - 8, rect.Height - 8)));
            dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
            dc.Pop();
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        foreach (var shape in DrawingShapes)
        {
            if (!shape.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == shape.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == shape.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(8, shape.Width),
                Math.Max(8, shape.Height));

            var rotationPushed = PushRotation(dc, shape.RotationDegrees, rect);
            var colors = ResolveDrawingShapeColors(shape, WorkbookTheme);
            DrawShapeThemeEffect(dc, shape.Kind, rect, WorkbookTheme);
            DrawShapeAuthoredEffect(dc, shape.Kind, rect, shape);
            var pen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1.5);
            pen.Freeze();
            var fill = CreateDrawingShapeFill(shape, colors.Fill);
            switch (shape.Kind)
            {
                case DrawingShapeKind.Rectangle:
                    dc.DrawRectangle(fill, pen, rect);
                    break;
                case DrawingShapeKind.Ellipse:
                    dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    break;
                case DrawingShapeKind.Line:
                    dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                    break;
            }
            if (rotationPushed) dc.Pop();
        }
    }

    private static Brush CreateDrawingShapeFill(DrawingShapeModel shape, CellColor startColor)
    {
        if (shape.GradientFillEndColor is { } endColor && shape.Kind != DrawingShapeKind.Line)
        {
            var brush = new LinearGradientBrush(
                Color.FromArgb(72, startColor.R, startColor.G, startColor.B),
                Color.FromArgb(72, endColor.R, endColor.G, endColor.B),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        return MakeBrushAlpha(32, startColor.R, startColor.G, startColor.B);
    }

    private static void DrawShapeAuthoredEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, DrawingShapeModel shape)
    {
        if (!shape.HasShadowEffect)
            return;

        var shadowRect = rect;
        shadowRect.Offset(3, 3);
        var shadowBrush = MakeBrushAlpha(58, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    private static void DrawTextBoxThemeEffect(DrawingContext dc, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        dc.DrawRectangle(MakeBrushAlpha(alpha, 0, 0, 0), null, shadowRect);
    }

    private static void DrawShapeThemeEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        var shadowBrush = MakeBrushAlpha(alpha, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme) =>
        new(
            shape.GetEffectiveFillColor(theme, new CellColor(31, 119, 180)),
            shape.GetEffectiveOutlineColor(theme, new CellColor(68, 68, 68)));

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme) =>
        new(
            textBox.GetEffectiveFillColor(theme, CellColor.White),
            textBox.GetEffectiveOutlineColor(theme, new CellColor(89, 89, 89)));

    private static bool PushRotation(DrawingContext dc, double rotationDegrees, Rect rect)
    {
        if (Math.Abs(rotationDegrees % 360) <= 0.0001)
            return false;

        dc.PushTransform(new RotateTransform(
            rotationDegrees,
            rect.Left + rect.Width / 2,
            rect.Top + rect.Height / 2));
        return true;
    }

    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 210, 210)), 0.75);
        var fill = Brushes.White;
        foreach (var picture in Pictures)
        {
            if (!picture.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == picture.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == picture.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(24, picture.Width),
                Math.Max(18, picture.Height));

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.PushTransform(new RotateTransform(
                    picture.RotationDegrees,
                    rect.Left + rect.Width / 2,
                    rect.Top + rect.Height / 2));

            if (picture.Kind == PictureKind.Image &&
                TryLoadPictureImage(picture, out var image))
            {
                if (HasPictureCrop(picture))
                {
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
                    dc.DrawRectangle(brush, null, rect);
                }
                else
                {
                    dc.DrawImage(image, rect);
                }
                dc.DrawRectangle(null, borderPen, rect);
                if (Math.Abs(picture.RotationDegrees) > 0.0001)
                    dc.Pop();
                continue;
            }

            dc.DrawRectangle(fill, borderPen, rect);

            var rows = Math.Max(1, picture.SourceRowCount);
            var cols = Math.Max(1, picture.SourceColumnCount);
            var cellWidth = rect.Width / cols;
            var cellHeight = rect.Height / rows;

            for (uint r = 1; r < rows; r++)
            {
                var y = rect.Top + r * cellHeight;
                dc.DrawLine(gridPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }

            for (uint c = 1; c < cols; c++)
            {
                var x = rect.Left + c * cellWidth;
                dc.DrawLine(gridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            }

            foreach (var cell in picture.Cells)
            {
                if (cell.RowOffset >= rows || cell.ColumnOffset >= cols || string.IsNullOrEmpty(cell.Text))
                    continue;
                var textRect = new Rect(
                    rect.Left + cell.ColumnOffset * cellWidth + 3,
                    rect.Top + cell.RowOffset * cellHeight + 1,
                    Math.Max(1, cellWidth - 6),
                    Math.Max(1, cellHeight - 2));
                var text = new FormattedText(
                    cell.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    DefaultTypeface,
                    11,
                    TextBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    MaxTextWidth = textRect.Width,
                    MaxTextHeight = textRect.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                dc.PushClip(new RectangleGeometry(textRect));
                dc.DrawText(text, textRect.TopLeft);
                dc.Pop();
            }

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.Pop();
        }
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private void RenderObjectPlaceholders(DrawingContext dc)
    {
        if (Viewport == null) return;

        if (Charts is not null)
        {
            var index = 1;
            foreach (var chart in Charts)
            {
                if (chart.IsVisible)
                    DrawObjectPlaceholder(dc, new Rect(
                        chart.Left + ActualRowHeaderWidth,
                        chart.Top + EffectiveColHeaderHeight,
                        Math.Max(24, chart.Width),
                        Math.Max(18, chart.Height)), CreateObjectPlaceholderLabel("Chart", chart.Name, index));
                index++;
            }
        }

        if (DrawingShapes is not null)
        {
            var index = 1;
            foreach (var shape in DrawingShapes)
            {
                if (shape.IsVisible && TryCreateAnchoredObjectRect(shape.Anchor, shape.Width, shape.Height, 8, 8, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Shape", shape.Name, index));
                index++;
            }
        }

        if (Pictures is not null)
        {
            var index = 1;
            foreach (var picture in Pictures)
            {
                if (picture.IsVisible && TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Picture", picture.Name, index));
                index++;
            }
        }

        if (TextBoxes is not null)
        {
            var index = 1;
            foreach (var textBox in TextBoxes)
            {
                if (textBox.IsVisible && TryCreateAnchoredObjectRect(textBox.Anchor, textBox.Width, textBox.Height, 24, 18, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Text Box", textBox.Name, index));
                index++;
            }
        }
    }

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index)
    {
        var fallback = index <= 1 ? objectType : $"{objectType} {index}";
        return string.IsNullOrWhiteSpace(objectName) ? fallback : objectName.Trim();
    }

    public bool TryCreateAnchoredObjectRect(
        CellAddress anchor,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out Rect rect)
    {
        rect = default;
        if (Viewport == null)
            return false;

        var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == anchor.Row);
        var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == anchor.Col);
        if (row is null || col is null)
            return false;

        rect = new Rect(
            col.LeftOffset + ActualRowHeaderWidth,
            row.TopOffset + EffectiveColHeaderHeight,
            Math.Max(minimumWidth, width),
            Math.Max(minimumHeight, height));
        return true;
    }

    private void DrawObjectPlaceholder(DrawingContext dc, Rect rect, string label)
    {
        dc.DrawRectangle(ObjectPlaceholderFill, ObjectPlaceholderPen, rect);
        DrawPlaceholderDiagonals(dc, rect);

        var text = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            11,
            ObjectPlaceholderTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 8),
            MaxTextHeight = Math.Max(1, rect.Height - 8),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var textPoint = new Point(
            rect.Left + Math.Max(4, (rect.Width - text.Width) / 2),
            rect.Top + Math.Max(4, (rect.Height - text.Height) / 2));
        dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, Math.Max(1, rect.Width - 8), Math.Max(1, rect.Height - 8))));
        dc.DrawText(text, textPoint);
        dc.Pop();
    }

    private static void DrawPlaceholderDiagonals(DrawingContext dc, Rect rect)
    {
        dc.DrawLine(ObjectPlaceholderPen, rect.TopLeft, rect.BottomRight);
        dc.DrawLine(ObjectPlaceholderPen, rect.TopRight, rect.BottomLeft);
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private void RenderWorksheetBackground(DrawingContext dc)
    {
        if (WorksheetBackground == null || !TryLoadWorksheetBackgroundImage(WorksheetBackground, out var image) || image == null)
            return;

        var brush = new ImageBrush(image)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, image.Width, image.Height),
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        dc.DrawRectangle(
            brush,
            null,
            new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, ActualWidth - ActualRowHeaderWidth), Math.Max(0, ActualHeight - EffectiveColHeaderHeight)));
    }

    private static bool TryLoadWorksheetBackgroundImage(WorksheetBackgroundImage background, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(background.ImageBytes, out image);

    private static bool TryLoadPictureImage(PictureModel picture, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(picture.ImageBytes, out image);
}

public readonly record struct DrawingObjectColors(CellColor Fill, CellColor Outline);
