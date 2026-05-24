using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 210, 210)), 0.75);
        var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 2);
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
                DrawPictureSelectionAdorner(dc, picture, rect, selectedPen);
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

            DrawPictureSelectionAdorner(dc, picture, rect, selectedPen);

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.Pop();
        }
    }

    private void DrawPictureSelectionAdorner(DrawingContext dc, PictureModel picture, Rect rect, Pen selectedPen)
    {
        if (SelectedRange?.Start != picture.Anchor)
            return;

        dc.DrawRectangle(null, selectedPen, rect);
        const double handle = 6;
        var handleBrush = new SolidColorBrush(Color.FromRgb(33, 115, 70));
        handleBrush.Freeze();
        foreach (var point in new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight })
        {
            dc.DrawRectangle(
                handleBrush,
                null,
                new Rect(point.X - handle / 2, point.Y - handle / 2, handle, handle));
        }
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

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
