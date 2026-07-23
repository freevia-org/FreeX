using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public sealed record ConditionalIconCellLayout(Rect IconRect, Rect TextRect, bool ShouldDrawText);

public partial class GridView
{
    public static ConditionalIconCellLayout CalculateConditionalIconCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon,
        bool isRightToLeft = false)
    {
        // R55-meta-1: the WPF adapter (ConditionalIconLayoutPlanner) predates the R54 RTL-mirroring
        // param, so we call the portable planner directly to thread isRightToLeft through — matching
        // how Excel mirrors icon-set glyphs to the cell's right edge on right-to-left sheets.
        var layout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            cellRect.Left,
            cellRect.Top,
            cellRect.Width,
            cellRect.Height,
            icon.ShowValue,
            isRightToLeft);

        var iconRect = new Rect(layout.IconLeft, layout.IconTop, layout.IconSize, layout.IconSize);

        if (!icon.ShowValue)
            return new ConditionalIconCellLayout(iconRect, Rect.Empty, ShouldDrawText: false);

        var textRect = new Rect(layout.TextLeft, cellRect.Top, layout.TextWidth, cellRect.Height);
        return new ConditionalIconCellLayout(iconRect, textRect, ShouldDrawText: layout.ShouldDrawText);
    }

    public static bool ShouldDrawCellContent(DisplayCell cell, CellAddress? editingCell)
    {
        if (editingCell is { } address && address.Row == cell.Row && address.Col == cell.Col)
            return false;

        return !string.IsNullOrEmpty(cell.DisplayText) || cell.ConditionalIcon is not null || cell.ConditionalDataBar is not null;
    }

    /// <summary>
    /// A cell that belongs to a merged region blocks overflow just like a cell that has real content —
    /// Excel never lets overflow text slide across a merged range, blank or not. Callers that can
    /// resolve merge membership for <paramref name="cell"/> should pass it via <paramref name="merge"/>
    /// (mirroring the <see cref="CanOverflowCellText"/> convention); it defaults to <see langword="null"/>
    /// for callers that don't have merge data on hand, preserving prior behavior for them.
    /// </summary>
    public static bool IsOverflowOccupied(DisplayCell cell, CellAddress? editingCell, GridRange? merge = null)
        => CellTextOverflowPlanner.IsOverflowOccupied(cell, editingCell, merge);

    /// <summary>
    /// Builds the set of cells that block text overflow. <paramref name="findMerge"/> (row, col) -&gt;
    /// the merged region containing that cell, or <see langword="null"/> when it isn't merged — lets
    /// callers with merge data mark a blank merged cell as occupied so overflow text stops at its
    /// boundary instead of sliding across it, matching Excel.
    /// </summary>
    public static HashSet<(uint Row, uint Col)> BuildOccupiedCellSet(
        IEnumerable<DisplayCell> cells,
        CellAddress? editingCell,
        Func<uint, uint, GridRange?>? findMerge = null)
    {
        var occupied = new HashSet<(uint Row, uint Col)>();
        foreach (var cell in cells)
        {
            var merge = findMerge?.Invoke(cell.Row, cell.Col);
            if (IsOverflowOccupied(cell, editingCell, merge))
                occupied.Add((cell.Row, cell.Col));
        }

        return occupied;
    }

    // Public (rather than private) so the WPF print/PDF path (PrintRenderer.GridCells.cs, a
    // different assembly) can draw the exact same icon glyph the interactive grid draws instead of
    // reimplementing ConditionalIconGlyphRenderer's (internal) geometry a second time.
    public static void DrawConditionalIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect) =>
        ConditionalIconGlyphRenderer.Draw(dc, icon, rect);

    public static ConditionalIconGlyphKind ResolveConditionalIconGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveGlyphKind(icon);

    public static string ResolveConditionalIconColor(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveColor(icon);
}
