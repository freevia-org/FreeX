using System.Windows;

namespace Freexcel.App.Host;

public sealed record FormulaInlineEditorLayout(Rect EditorRect, Rect TextOverlayRect);

public static class FormulaInlineEditorLayoutPlanner
{
    private const double MinimumEditorWidth = 160;

    public static FormulaInlineEditorLayout Create(double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var editorRect = new Rect(
            cellLeft - 2,
            cellTop - 2,
            Math.Max(cellWidth + 4, MinimumEditorWidth),
            Math.Max(cellHeight + 6, 24));

        var textOverlayRect = new Rect(
            editorRect.Left + 4,
            editorRect.Top + 3,
            Math.Max(editorRect.Width - 8, 0),
            Math.Max(editorRect.Height - 6, 18));

        return new FormulaInlineEditorLayout(editorRect, textOverlayRect);
    }
}
