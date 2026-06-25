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
        double width, height;
        if (xfrmWidthPixels is > 0 && xfrmHeightPixels is > 0)
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
        if (height > 0)
            shape.Height = height;
        shape.AnchorOffsetX = anchor.FromColumnOffset;
        shape.AnchorOffsetY = anchor.FromRowOffset;
    }

    private static (double Width, double Height) GetAnchorSize(XlsxDrawingAnchor anchor, Sheet sheet)
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
