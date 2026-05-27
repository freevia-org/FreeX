using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private static readonly Pen PictureBorderPen = CreateFrozenPen(MakeBrush(120, 120, 120), 1);
    private static readonly Pen PictureGridPen = CreateFrozenPen(MakeBrush(210, 210, 210), 0.75);
    private static readonly Brush PictureSelectionBrush = MakeBrush(33, 115, 70);
    private static readonly Pen PictureSelectionPen = CreateFrozenPen(PictureSelectionBrush, 2);

    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var fill = Brushes.White;
        foreach (var picture in Pictures)
        {
            if (!picture.IsVisible) continue;
            if (!TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect))
                continue;

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
                dc.DrawRectangle(null, PictureBorderPen, rect);
                DrawPictureSelectionAdorner(dc, picture, rect);
                if (Math.Abs(picture.RotationDegrees) > 0.0001)
                    dc.Pop();
                continue;
            }

            dc.DrawRectangle(fill, PictureBorderPen, rect);

            var rows = Math.Max(1, picture.SourceRowCount);
            var cols = Math.Max(1, picture.SourceColumnCount);
            var cellWidth = rect.Width / cols;
            var cellHeight = rect.Height / rows;

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

            DrawPictureSelectionAdorner(dc, picture, rect);

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.Pop();
        }
    }

    private void DrawPictureSelectionAdorner(DrawingContext dc, PictureModel picture, Rect rect)
    {
        if (SelectedRange?.Start != picture.Anchor)
            return;

        dc.DrawRectangle(null, PictureSelectionPen, rect);
        const double handle = 6;
        foreach (var point in new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight })
        {
            dc.DrawRectangle(
                PictureSelectionBrush,
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
