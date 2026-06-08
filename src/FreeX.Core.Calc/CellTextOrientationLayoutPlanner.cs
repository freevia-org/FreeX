using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public readonly record struct CellTextLayoutPoint(double X, double Y);

public readonly record struct CellTextLayoutRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct CellTextOrientationLayout(
    CellTextLayoutPoint TextPoint,
    CellTextLayoutRect Bounds,
    double TransformAngle)
{
    public bool IsRotated => Math.Abs(TransformAngle) > 0.001;
}

public static class CellTextOrientationLayoutPlanner
{
    public static bool HasTextOrientation(int textRotation) =>
        IsStackedTextRotation(textRotation) || NormalizeRotationForDisplay(textRotation) != 0;

    public static bool IsStackedTextRotation(int textRotation) => textRotation == 255;

    public static int NormalizeRotationForDisplay(int textRotation) =>
        textRotation is >= -90 and <= 90 ? textRotation : 0;

    public static string PrepareDisplayText(string text, int textRotation)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!IsStackedTextRotation(textRotation) || text.Length <= 1)
            return text;

        var stacked = new StringBuilder(text.Length * 2 - 1);
        foreach (var character in text)
        {
            if (stacked.Length > 0)
                stacked.Append('\n');

            stacked.Append(character);
        }

        return stacked.ToString();
    }

    public static CellTextOrientationLayout CalculateLayout(
        CellTextLayoutRect cellRect,
        double textWidth,
        double textHeight,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment? verticalAlignment,
        bool isNumeric,
        double indentPixels,
        int textRotation)
    {
        var displayRotation = NormalizeRotationForDisplay(textRotation);
        var transformAngle = -displayRotation;
        var boundsWidth = textWidth;
        var boundsHeight = textHeight;
        var minX = 0.0;
        var minY = 0.0;

        if (displayRotation != 0)
        {
            var radians = transformAngle * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var x1 = textWidth * cos;
            var y1 = textWidth * sin;
            var x2 = -textHeight * sin;
            var y2 = textHeight * cos;
            var x3 = x1 + x2;
            var y3 = y1 + y2;

            minX = Math.Min(0, Math.Min(x1, Math.Min(x2, x3)));
            minY = Math.Min(0, Math.Min(y1, Math.Min(y2, y3)));
            var maxX = Math.Max(0, Math.Max(x1, Math.Max(x2, x3)));
            var maxY = Math.Max(0, Math.Max(y1, Math.Max(y2, y3)));
            boundsWidth = maxX - minX;
            boundsHeight = maxY - minY;
        }

        var boundsX = horizontalAlignment switch
        {
            HorizontalAlignment.Right => cellRect.Right - Math.Min(boundsWidth, cellRect.Width - 2) - 2,
            HorizontalAlignment.Justify or HorizontalAlignment.Distributed => cellRect.Left + (cellRect.Width - boundsWidth) / 2,
            HorizontalAlignment.Center => cellRect.Left + (cellRect.Width - boundsWidth) / 2,
            HorizontalAlignment.General when isNumeric => cellRect.Right - Math.Min(boundsWidth, cellRect.Width - 2) - 2,
            _ => cellRect.Left + 2 + indentPixels
        };
        var boundsY = verticalAlignment switch
        {
            VerticalAlignment.Top => cellRect.Top + 1,
            VerticalAlignment.Center => cellRect.Top + (cellRect.Height - boundsHeight) / 2,
            VerticalAlignment.Bottom => cellRect.Bottom - boundsHeight - 1,
            _ => cellRect.Top + (cellRect.Height - boundsHeight) / 2
        };
        boundsY = Math.Max(cellRect.Top, boundsY);

        var textPoint = new CellTextLayoutPoint(
            Math.Round(boundsX - minX),
            Math.Round(boundsY - minY));
        var bounds = new CellTextLayoutRect(
            textPoint.X + minX,
            textPoint.Y + minY,
            boundsWidth,
            boundsHeight);

        return new CellTextOrientationLayout(textPoint, bounds, transformAngle);
    }

    public static bool ShouldClip(
        bool wrapText,
        CellTextLayoutRect clipRect,
        double textHeight,
        CellTextOrientationLayout textLayout,
        double tolerance = 0.5)
    {
        if (wrapText && textHeight > clipRect.Height + tolerance)
            return true;

        return textLayout.Bounds.Left < clipRect.Left - tolerance ||
            textLayout.Bounds.Top < clipRect.Top - tolerance ||
            textLayout.Bounds.Right > clipRect.Right + tolerance ||
            textLayout.Bounds.Bottom > clipRect.Bottom + tolerance;
    }
}
