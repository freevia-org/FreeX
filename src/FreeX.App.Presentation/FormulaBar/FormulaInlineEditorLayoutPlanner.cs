namespace FreeX.App.Presentation.FormulaBar;

public sealed record FormulaInlineEditorLayout(FormulaEditorRect EditorRect, FormulaEditorRect TextOverlayRect);
public readonly record struct FormulaInlineEditorOverflow(bool Left, bool Right)
{
    public static FormulaInlineEditorOverflow None => new(false, false);
}

public static class FormulaInlineEditorLayoutPlanner
{
    private const double TextSurfaceTrailingBuffer = 16;
    private const double SelectionLikeBorderThickness = 1;
    private const double HiddenBorderCover = 2;

    public static FormulaInlineEditorLayout Create(
        double cellLeft,
        double cellTop,
        double cellWidth,
        double cellHeight,
        double desiredTextWidth = 0,
        double availableRight = double.PositiveInfinity,
        int lineCount = 1)
    {
        // R78-render-inplace-editor-5-3: Alt+Enter-inserted line breaks (and any pre-existing
        // multi-line cell text) must grow the editor box downward the same way long single-line
        // text already grows it sideways -- otherwise line 2+ is typed/shown "blind", clipped
        // below the fixed single-row height. One cell-row height per line is the same unit Excel's
        // own auto-height uses for wrapped/broken text, so growth lands on whole-row boundaries.
        var effectiveHeight = cellHeight * Math.Max(1, lineCount);

        var editorRect = new FormulaEditorRect(
            cellLeft,
            cellTop,
            cellWidth,
            effectiveHeight);

        var textLeft = editorRect.Left + 4;
        var textWidth = Math.Max(0, editorRect.Width - 8);
        if (desiredTextWidth > 0)
            textWidth = Math.Max(textWidth, desiredTextWidth + TextSurfaceTrailingBuffer);

        if (double.IsFinite(availableRight))
            textWidth = Math.Min(textWidth, Math.Max(0, availableRight - textLeft));

        var textOverlayRect = new FormulaEditorRect(
            textLeft,
            editorRect.Top,
            textWidth,
            editorRect.Height);

        return new FormulaInlineEditorLayout(editorRect, textOverlayRect);
    }

    public static FormulaEditorRect GetChromeRect(FormulaEditorRect editorRect, FormulaInlineEditorOverflow overflow)
    {
        var left = editorRect.Left;
        var width = editorRect.Width;

        if (overflow.Left)
        {
            left -= HiddenBorderCover;
            width += HiddenBorderCover;
        }

        if (overflow.Right)
            width += HiddenBorderCover;

        return new FormulaEditorRect(left, editorRect.Top, width, editorRect.Height);
    }

    public static FormulaEditorThickness GetChromeBorderThickness(FormulaInlineEditorOverflow overflow) =>
        new(
            overflow.Left ? 0 : SelectionLikeBorderThickness,
            SelectionLikeBorderThickness,
            overflow.Right ? 0 : SelectionLikeBorderThickness,
            SelectionLikeBorderThickness);
}
