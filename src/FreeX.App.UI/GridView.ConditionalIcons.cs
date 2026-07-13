using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public sealed record ConditionalIconCellLayout(Rect IconRect, Rect TextRect, bool ShouldDrawText);

public partial class GridView
{
    public static ConditionalIconCellLayout CalculateConditionalIconCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.CalculateCellLayout(cellRect, icon);

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
    {
        if (editingCell is { } address && address.Row == cell.Row && address.Col == cell.Col)
            return true;

        if (merge is not null)
            return true;

        return !string.IsNullOrEmpty(cell.DisplayText) ||
               cell.ConditionalIcon is not null ||
               cell.ConditionalDataBar is not null ||
               cell.Formula is not null ||
               cell.RawValue is not null and not BlankValue;
    }

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

    private static void DrawConditionalIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect) =>
        ConditionalIconGlyphRenderer.Draw(dc, icon, rect);

    public static ConditionalIconGlyphKind ResolveConditionalIconGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveGlyphKind(icon);

    public static string ResolveConditionalIconColor(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveColor(icon);
}
