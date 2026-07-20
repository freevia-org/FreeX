using System;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Framework-neutral geometry for laying an icon-set glyph (and the cell text that follows it) into a
/// cell, expressed as plain doubles in the cell's own coordinate space. No UI-framework types so it can
/// be unit-tested and reused across every shell. <see cref="ShouldDrawText"/> is <c>false</c> when the
/// rule hides the value or the remaining text area is empty.
/// </summary>
public readonly record struct ConditionalIconCellLayout(
    double IconSize,
    double IconLeft,
    double IconTop,
    double TextLeft,
    double TextWidth,
    bool ShouldDrawText);

/// <summary>
/// Portable, single-source layout of a conditional-format icon-set glyph within a cell. The glyph is a
/// fixed <see cref="IconSize"/> square, inset <see cref="IconLeftInset"/> from the cell's left edge and
/// vertically centered, then clamped to stay inside small cells. When the rule shows the value, the cell
/// text starts after a fixed <see cref="GutterWidth"/> gutter. This is the source of truth previously
/// inlined in the desktop <c>ConditionalIconLayoutPlanner</c> and re-declared as local constants in the
/// cross-platform port's render planner.
/// </summary>
public static class ConditionalIconCellLayoutPlanner
{
    /// <summary>Nominal edge length (device pixels at 100% zoom) of an icon-set glyph.</summary>
    public const double IconSize = 10d;

    /// <summary>Inset (device pixels at 100% zoom) of the glyph from the cell's left edge.</summary>
    public const double IconLeftInset = 4d;

    /// <summary>Width (device pixels at 100% zoom) of the gutter reserved before cell text.</summary>
    public const double GutterWidth = 20d;

    /// <summary>
    /// Compute the neutral icon/text geometry for a cell. Coordinates are in the same space as the
    /// supplied cell rectangle; the glyph size is clamped so it never overflows narrow or short cells,
    /// and its left origin is clamped to keep the glyph inside the cell.
    /// </summary>
    /// <param name="cellLeft">Left edge of the cell.</param>
    /// <param name="cellTop">Top edge of the cell.</param>
    /// <param name="cellWidth">Cell width (≥ 0).</param>
    /// <param name="cellHeight">Cell height (≥ 0).</param>
    /// <param name="showValue">Whether the rule also draws the cell value next to the glyph.</param>
    /// <param name="isRightToLeft">
    /// R54-render-cf-icon-databar-4-3: <c>true</c> when the sheet's reading order is right-to-left
    /// (<c>Sheet.IsRightToLeft</c>). Excel mirrors icon-set glyphs to the cell's RIGHT edge (with the
    /// value pushed toward the left) on an RTL sheet, matching how it already mirrors data bars (see
    /// <c>ViewportConditionalFormatEvaluator.Thresholds.cs</c>'s <c>MirrorDataBarIfRightToLeft</c>), row
    /// headers, and cell alignment. Defaults to <c>false</c> (the pre-existing left-pinned layout) for
    /// callers that don't yet pass the sheet's reading order.
    /// </param>
    public static ConditionalIconCellLayout CalculateCellLayout(
        double cellLeft,
        double cellTop,
        double cellWidth,
        double cellHeight,
        bool showValue,
        bool isRightToLeft = false)
    {
        var cellRight = cellLeft + cellWidth;

        var size = Math.Min(
            IconSize,
            Math.Min(
                Math.Max(0, cellWidth - 8),
                Math.Max(0, cellHeight - 6)));
        var iconLeft = isRightToLeft
            ? Math.Round(Math.Clamp(cellRight - IconLeftInset - size, cellLeft, cellRight - size))
            : Math.Round(Math.Clamp(cellLeft + IconLeftInset, cellLeft, cellRight - size));
        var iconTop = Math.Round(cellTop + (cellHeight - size) / 2);

        if (!showValue)
            return new ConditionalIconCellLayout(size, iconLeft, iconTop, TextLeft: 0, TextWidth: 0, ShouldDrawText: false);

        double textLeft;
        double textWidth;
        if (isRightToLeft)
        {
            // Mirror image of the LTR layout: the gutter is reserved next to the (right-pinned) icon,
            // so the value text runs from the cell's left edge up to where that gutter begins.
            textLeft = cellLeft;
            var textRight = Math.Max(cellLeft, cellRight - GutterWidth);
            textWidth = Math.Max(0, textRight - cellLeft);
        }
        else
        {
            textLeft = Math.Min(cellRight, cellLeft + GutterWidth);
            textWidth = Math.Max(0, cellRight - textLeft);
        }

        return new ConditionalIconCellLayout(
            size,
            iconLeft,
            iconTop,
            textLeft,
            textWidth,
            ShouldDrawText: textWidth > 0 && cellHeight > 0);
    }
}
