using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDrawingAnchorApplier
{
    public static void ApplyToChart(ChartModel chart, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        chart.DrawingAnchorKind = anchor.Kind;
        chart.Left = anchor.AbsoluteLeft ?? (SumColumnPixels(sheet, 1, anchor.FromColumnZeroBased) + anchor.FromColumnOffset);
        chart.Top = anchor.AbsoluteTop ?? (SumRowPixels(sheet, 1, anchor.FromRowZeroBased) + anchor.FromRowOffset);

        var width = anchor.Width ?? (
            SumColumnPixels(sheet, anchor.FromColumnZeroBased + 1, anchor.ToColumnZeroBased!.Value - anchor.FromColumnZeroBased)
            + anchor.ToColumnOffset!.Value
            - anchor.FromColumnOffset);
        var height = anchor.Height ?? (
            SumRowPixels(sheet, anchor.FromRowZeroBased + 1, anchor.ToRowZeroBased!.Value - anchor.FromRowZeroBased)
            + anchor.ToRowOffset!.Value
            - anchor.FromRowOffset);
        if (width > 0)
            chart.Width = width;
        if (height > 0)
            chart.Height = height;
    }

    public static void ApplyToPicture(PictureModel picture, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        var (width, height) = GetAnchorSize(anchor, sheet);
        if (width > 0)
            picture.Width = width;
        if (height > 0)
            picture.Height = height;
        picture.AnchorOffsetX = anchor.FromColumnOffset;
        picture.AnchorOffsetY = anchor.FromRowOffset;
    }

    public static void ApplyToTextBox(TextBoxModel textBox, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        var (width, height) = GetAnchorSize(anchor, sheet);
        if (width > 0)
            textBox.Width = width;
        if (height > 0)
            textBox.Height = height;
        textBox.AnchorOffsetX = anchor.FromColumnOffset;
        textBox.AnchorOffsetY = anchor.FromRowOffset;
    }

    public static void ApplyToShape(
        DrawingShapeModel shape,
        XlsxDrawingAnchor? anchor,
        Sheet sheet,
        double? xfrmWidthPixels = null,
        double? xfrmHeightPixels = null)
    {
        if (anchor is null)
            return;

        // Prefer the pre-rotation size from <a:xfrm><a:ext cx cy> when available.
        // For rotated shapes the outer anchor extent is the bounding box of the rotated shape,
        // not the shape's own unrotated dimensions, so we must use the xfrm extent instead.
        // For line-like shapes (Line, ElbowConnector, CurvedConnector), the xfrm cx/cy may be
        // zero along one axis (e.g. a perfectly horizontal line has cy=0); in that case we still
        // prefer the xfrm values so the shape renders as a flat line rather than a diagonal.
        double width, height;
        var isLineLike = DrawingShapeKindSupport.IsLineLike(shape.Kind);
        if (xfrmWidthPixels.HasValue && xfrmHeightPixels.HasValue &&
            (xfrmWidthPixels is > 0 || isLineLike) &&
            (xfrmHeightPixels is > 0 || isLineLike))
        {
            width = xfrmWidthPixels.Value;
            height = xfrmHeightPixels.Value;
        }
        else
        {
            (width, height) = GetAnchorSize(anchor, sheet);
        }

        if (width > 0)
            shape.Width = width;
        if (isLineLike)
            shape.Height = Math.Max(0, height);   // allow flat (zero-height) lines
        else if (height > 0)
            shape.Height = height;
        shape.AnchorOffsetX = anchor.FromColumnOffset;
        shape.AnchorOffsetY = anchor.FromRowOffset;
    }

    /// <summary>
    /// Computes the effective rendered width/height (in DIP pixels) for a drawing anchor, preferring the
    /// anchor's own explicit extent (oneCellAnchor/absoluteAnchor <c>ext</c>, or precomputed EMU->pixel width/
    /// height) and otherwise deriving it from the twoCellAnchor's to/from cell span. Shared by both the
    /// load-time anchor applier and the patch-safe source-vs-current geometry comparison, so a resize is
    /// measured identically whichever path is exercised.
    /// </summary>
    internal static (double Width, double Height) GetAnchorSize(XlsxDrawingAnchor anchor, Sheet sheet)
    {
        var width = anchor.Width ?? (
            SumColumnPixels(sheet, anchor.FromColumnZeroBased + 1, anchor.ToColumnZeroBased!.Value - anchor.FromColumnZeroBased)
            + anchor.ToColumnOffset!.Value
            - anchor.FromColumnOffset);
        var height = anchor.Height ?? (
            SumRowPixels(sheet, anchor.FromRowZeroBased + 1, anchor.ToRowZeroBased!.Value - anchor.FromRowZeroBased)
            + anchor.ToRowOffset!.Value
            - anchor.FromRowOffset);
        return (width, height);
    }

    private static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var col = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(col))
                width += sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

    private static double SumRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
        }

        return height;
    }
}
