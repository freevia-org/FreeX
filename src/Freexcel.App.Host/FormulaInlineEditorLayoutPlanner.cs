using System.Windows;

namespace Freexcel.App.Host;

public sealed record FormulaInlineEditorLayout(Rect EditorRect, Rect TextOverlayRect);

public static class FormulaInlineEditorLayoutPlanner
{
    private const double MinimumTextSurfaceWidth = 160;

    public static FormulaInlineEditorLayout Create(double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var editorRect = new Rect(
            cellLeft,
            cellTop,
            cellWidth,
            cellHeight);

        var textOverlayRect = new Rect(
            editorRect.Left + 4,
            editorRect.Top,
            Math.Max(editorRect.Width - 8, MinimumTextSurfaceWidth),
            editorRect.Height);

        return new FormulaInlineEditorLayout(editorRect, textOverlayRect);
    }

    public static Thickness GetChromeBorderThickness(bool textSpillsRight) =>
        textSpillsRight
            ? new Thickness(2, 2, 0, 2)
            : new Thickness(2);
}
