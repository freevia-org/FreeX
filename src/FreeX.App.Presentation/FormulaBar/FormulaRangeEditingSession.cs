using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

public readonly record struct FormulaReferenceEntrySpan(int Start, int Length);

public readonly record struct FormulaRangeSelectionPlan(
    GridRange Range,
    CellAddress Anchor,
    CellAddress Cursor);

/// <summary>
/// Owns the renderer-neutral state of an active formula point-entry interaction. Editors, focus,
/// overlays, pointer capture, and workbook mutations remain responsibilities of the UI host.
/// </summary>
public sealed class FormulaRangeEditingSession
{
    public bool PointMode { get; private set; }

    public ExcelSelectionMode SelectionMode { get; private set; } = ExcelSelectionMode.Normal;

    public FormulaReferenceEntrySpan? ReferenceSpan { get; private set; }

    public CellAddress? SelectionAnchor { get; private set; }

    public CellAddress? SelectionCursor { get; private set; }

    public FormulaSheetSpanEntryState SheetSpan { get; private set; } = FormulaSheetSpanEntryState.Empty;

    public bool IsRangeEntryActive(string? text) =>
        FormulaEditInteractionPlanner.IsRangeEntryActive(text, PointMode);

    public void Reset()
    {
        PointMode = false;
        SelectionMode = ExcelSelectionMode.Normal;
        ClearSelection();
        ClearReferenceSpan();
    }

    public void ClearSelection()
    {
        SelectionAnchor = null;
        SelectionCursor = null;
    }

    public void ClearReferenceSpan()
    {
        ReferenceSpan = null;
        SheetSpan = FormulaSheetSpanEntryState.Empty;
    }

    public void SetPointMode(bool pointMode) => PointMode = pointMode;

    public void SetPointModeForFormulaText(string? text) =>
        PointMode = FormulaEditInteractionPlanner.IsFormulaText(text);

    public FormulaEditTextChangePlan ApplyTextChanged(string? text)
    {
        var plan = FormulaEditInteractionPlanner.BuildTextChangePlan(text);
        if (plan.StartsPointMode)
            PointMode = true;

        return plan;
    }

    public FormulaTypedEntryPlan ApplyTypedEntry(string? text)
    {
        var plan = FormulaEditInteractionPlanner.BuildTypedEntryPlan(text);
        PointMode = plan.PointMode;
        return plan;
    }

    public FormulaPointModeTogglePlan TogglePointMode(string? text)
    {
        var plan = FormulaEditInteractionPlanner.BuildPointModeTogglePlan(text, PointMode);
        PointMode = plan.PointMode;
        if (plan.ClearReferenceSpan)
            ClearReferenceSpan();

        return plan;
    }

    public bool TryToggleSelectionMode(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers)
    {
        if (!FormulaRangeEntryPlanner.TryToggleKeyboardSelectionMode(
                key,
                modifiers,
                SelectionMode,
                out var next))
        {
            return false;
        }

        SelectionMode = next;
        return true;
    }

    public FormulaRangeSelectionPlan PlanSelection(CellAddress target, bool extendSelection)
    {
        var anchor = extendSelection && SelectionAnchor is { } existingAnchor
            ? existingAnchor
            : target;
        var range = new GridRange(
            new CellAddress(
                target.Sheet,
                Math.Min(anchor.Row, target.Row),
                Math.Min(anchor.Col, target.Col)),
            new CellAddress(
                target.Sheet,
                Math.Max(anchor.Row, target.Row),
                Math.Max(anchor.Col, target.Col)));

        return new FormulaRangeSelectionPlan(range, anchor, target);
    }

    public void TrackSelection(CellAddress anchor, CellAddress cursor)
    {
        SelectionAnchor = anchor;
        SelectionCursor = cursor;
    }

    public void TrackReferenceSpan(int? start, int? length)
    {
        ReferenceSpan = start is { } referenceStart &&
            length is { } referenceLength &&
            referenceStart >= 0 &&
            referenceLength >= 0
                ? new FormulaReferenceEntrySpan(referenceStart, referenceLength)
                : null;
    }

    public void ApplyPlannerEdit(FormulaRangeEntryEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        TrackReferenceSpan(edit.ReferenceStart, edit.ReferenceLength);
    }

    public void ApplyPlannerEdit(
        FormulaRangeEntryEdit edit,
        CellAddress selectionAnchor,
        CellAddress selectionCursor)
    {
        ApplyPlannerEdit(edit);
        TrackSelection(selectionAnchor, selectionCursor);
    }

    public FormulaSheetSpanEntryState ApplySheetTabSelection(
        string activeSheetName,
        string clickedSheetName,
        bool shiftHeld)
    {
        SheetSpan = FormulaSheetSpanEntryPlanner.PlanTabSelection(
            SheetSpan,
            activeSheetName,
            clickedSheetName,
            shiftHeld);
        return SheetSpan;
    }
}
