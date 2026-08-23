using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Applies the renderer-neutral portion of imported clipboard table-cell styling.</summary>
public static class ClipboardTableCellStylePolicy
{
    public static void ApplyCore(TableCell cell, InCanvasRichClipboardTableCellStyle style)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(style);

        if (style.FillPattern is { Length: > 0 } pattern)
        {
            cell.Fill = new ShapeFill.Pattern(
                pattern,
                new ThemeAwareColor(SrgbColor.FromRgb(style.FillForegroundRgb ?? style.FillRgb ?? 0)),
                new ThemeAwareColor(SrgbColor.FromRgb(style.FillBackgroundRgb ?? style.FillRgb ?? 0xFFFFFF)));
        }
        else if (style.FillRgb is { } fillRgb)
        {
            cell.Fill = new ShapeFill.Solid(SrgbColor.FromRgb(fillRgb));
        }

        cell.Anchor = style.Anchor;
        if (style.TextVerticalType is { } textVerticalType && cell.TextBody is { } body)
            body.VerticalType = textVerticalType;
        cell.InsetLeftPt = style.InsetLeftPt;
        cell.InsetRightPt = style.InsetRightPt;
        cell.InsetTopPt = style.InsetTopPt;
        cell.InsetBottomPt = style.InsetBottomPt;
        cell.HMerge = style.HorizontalMergeContinuation;
        cell.VMerge = style.VerticalMergeContinuation;
        if (style.HorizontalMergeStart)
            cell.GridSpan = 2;
        if (style.VerticalMergeStart)
            cell.RowSpan = 2;
    }
}
