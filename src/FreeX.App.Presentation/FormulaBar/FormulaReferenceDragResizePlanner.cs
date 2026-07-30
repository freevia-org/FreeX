using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

/// <summary>
/// Portable, UI-free planner behind dragging a formula's range-highlight overlay corner to resize
/// the reference it represents (e.g. dragging <c>=SUM(A1:B2)</c>'s highlighted box down to C3
/// changes the formula to <c>=SUM(A1:C3)</c>), matching Excel's own point-and-drag reference editing.
/// </summary>
/// <remarks>
/// Kept free of any shell/UI-framework type so the shell only needs to translate a mouse-drag's target
/// cell into a <see cref="CellAddress"/> and hand it here; the reference-text rewrite mirrors
/// <see cref="FormulaRangeEntryPlanner.TryApplyRangeSelection"/> (used for the "drag to pick a new
/// range" point-mode flow) so a resized reference always renders with the same bare, current-sheet,
/// non-absolute style as a freshly re-picked one.
/// </remarks>
/// <remarks>
/// R92-meta-2: as of this writing only one desktop shell's host window
/// (<c>MainWindow.FormulaReferenceEditing.cs</c>) hooks the pointer-drag events on its highlight
/// overlay into this planner. The cross-platform shell's own host window already renders the same
/// range-highlight boxes (<c>AddFormulaReferenceHighlightOverlay</c>) but has no drag-grip/
/// pointer-capture wiring onto them yet -- adding that is tracked as a follow-up rather than
/// claimed as already shipped here.
/// </remarks>
public static class FormulaReferenceDragResizePlanner
{
    /// <summary>
    /// Computes the resized range: the corner opposite the one being dragged stays fixed, and the
    /// dragged corner becomes wherever the mouse currently is, normalized so Start/End stay top-
    /// left/bottom-right regardless of drag direction (dragging up/left past the fixed corner is
    /// allowed, matching Excel's own corner-drag behavior).
    /// </summary>
    public static GridRange ComputeResizedRange(CellAddress fixedCorner, CellAddress draggedCorner)
    {
        var sheet = fixedCorner.Sheet;
        return new GridRange(
            new CellAddress(sheet, Math.Min(fixedCorner.Row, draggedCorner.Row), Math.Min(fixedCorner.Col, draggedCorner.Col)),
            new CellAddress(sheet, Math.Max(fixedCorner.Row, draggedCorner.Row), Math.Max(fixedCorner.Col, draggedCorner.Col)));
    }

    /// <summary>
    /// Rewrites the formula text: replaces the original reference's full token span (<paramref
    /// name="textStart"/>/<paramref name="textLength"/>, as recorded by
    /// <see cref="FormulaReferenceHighlightPlanner"/>) with the freshly formatted resized range, and
    /// returns the caret index that lands right after the newly inserted reference text.
    /// </summary>
    public static (string Text, int CaretIndex) ApplyResize(
        string text,
        int textStart,
        int textLength,
        GridRange newRange,
        bool useR1C1ReferenceStyle)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (textStart < 0 || textLength < 0 || textStart + textLength > text.Length)
            throw new ArgumentOutOfRangeException(nameof(textStart));

        var referenceText = SpreadsheetDisplayFormatter.FormatRangeReference(
            newRange.Start, newRange.End, useR1C1ReferenceStyle);
        var newText = string.Concat(
            text.AsSpan(0, textStart),
            referenceText,
            text.AsSpan(textStart + textLength));
        return (newText, textStart + referenceText.Length);
    }
}
