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
    /// <summary>
    /// Resolves a cell's effective reading order given its own <see cref="CellStyle.ReadingOrder"/>
    /// override and the hosting sheet's <see cref="Sheet.IsRightToLeft"/> flag, matching Excel's
    /// Format Cells &gt; Alignment &gt; Text direction semantics: <see cref="CellReadingOrder.Context"/>
    /// (readingOrder="0", the default) follows the sheet's direction, while <see cref="CellReadingOrder.LeftToRight"/>
    /// / <see cref="CellReadingOrder.RightToLeft"/> force a direction regardless of the sheet.
    /// </summary>
    public static bool ResolveIsEffectivelyRightToLeft(CellReadingOrder readingOrder, bool sheetIsRightToLeft) =>
        readingOrder switch
        {
            CellReadingOrder.LeftToRight => false,
            CellReadingOrder.RightToLeft => true,
            _ => sheetIsRightToLeft
        };

    /// <summary>
    /// Resolves <see cref="HorizontalAlignment.General"/> to a concrete Left/Right alignment the way
    /// Excel does: numeric/date/error content general-aligns to the "end" of the effective reading
    /// direction (right when left-to-right, left when right-to-left) while text content general-aligns
    /// to the "start" (left when left-to-right, right when right-to-left). Non-General alignments are
    /// returned unchanged — Left/Right/Center/Justify/Distributed/Fill do not mirror with reading order.
    /// </summary>
    public static HorizontalAlignment ResolveEffectiveHorizontalAlignment(
        HorizontalAlignment horizontalAlignment,
        bool isNumeric,
        bool isEffectivelyRightToLeft)
    {
        if (horizontalAlignment != HorizontalAlignment.General)
            return horizontalAlignment;

        return (isNumeric, isEffectivelyRightToLeft) switch
        {
            (true, false) => HorizontalAlignment.Right,
            (true, true) => HorizontalAlignment.Left,
            (false, false) => HorizontalAlignment.Left,
            (false, true) => HorizontalAlignment.Right,
        };
    }

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
        int textRotation,
        bool isEffectivelyRightToLeft = false)
    {
        // General alignment (and only General) mirrors with the effective reading order: numeric
        // content anchors right in LTR / left in RTL, text content anchors left in LTR / right in
        // RTL. Non-General alignments (Left/Right/Center/...) are explicit user choices and do not
        // mirror — this matches Excel, which only auto-mirrors the "General" alignment.
        var effectiveHorizontalAlignment = ResolveEffectiveHorizontalAlignment(
            horizontalAlignment, isNumeric, isEffectivelyRightToLeft);

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

        var boundsX = effectiveHorizontalAlignment switch
        {
            // Right (explicit, or General resolved to Right — numeric content in an LTR context, or
            // text content in an RTL context) anchors its RIGHT edge at the cell's right edge (minus a
            // 2px pad).  When the text is wider than the cell, the left edge therefore lands to the
            // LEFT of the cell — i.e. the text overflows leftward, exactly like Excel.  Do NOT clamp
            // the position to keep the text inside the cell: clamping pins a too-wide right-aligned
            // string to the LEFT edge so it spills RIGHTWARD into the next column (a visible bug).
            HorizontalAlignment.Right => cellRect.Right - boundsWidth - 2,
            HorizontalAlignment.Justify or HorizontalAlignment.Distributed => cellRect.Left + (cellRect.Width - boundsWidth) / 2,
            HorizontalAlignment.Center => cellRect.Left + (cellRect.Width - boundsWidth) / 2,
            // Fill: text is repeated to fill width — the layout origin is still Left+2; rendering clips/repeats.
            HorizontalAlignment.Fill => cellRect.Left + 2,
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
