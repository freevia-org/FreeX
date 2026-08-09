using FreeX.App.Presentation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

public readonly record struct FormulaReferenceEntrySpan(int Start, int Length);

public readonly record struct FormulaRangeSelectionPlan(
    GridRange Range,
    CellAddress Anchor,
    CellAddress Cursor);

public readonly record struct FormulaRangeEditorSnapshot(
    string Text,
    int CaretIndex,
    int SelectionLength,
    CellAddress FormulaCell,
    bool UseR1C1ReferenceStyle,
    string? SelectedSheetName = null,
    string? SelectedWorkbookName = null);

public readonly record struct FormulaRangeSelectionEditPlan(
    FormulaRangeEntryEdit Edit,
    GridRange Range,
    CellAddress Anchor,
    CellAddress Cursor,
    bool UpdateLocalSelection);

public readonly record struct FormulaRangeKeyboardNavigationPlan(
    CellAddress Current,
    CellAddress Target,
    bool ExtendSelection);

public readonly record struct FormulaRangeSelectionModeChangePlan(
    ExcelSelectionMode Mode,
    FormulaEditStatusBarPlan? EditStatusBarPlan,
    string? StatusBarModeResourceKey);

public enum FormulaEditorSurfaceKind
{
    Inline,
    FormulaBar,
}

public readonly record struct FormulaCellValueAutocompletePlan(
    string Text,
    int SelectionStart,
    int SelectionLength);

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

    public FormulaReferenceHighlight? ReferenceDragHighlight { get; private set; }

    public bool IsReferenceDragActive => ReferenceDragHighlight is not null;

    public IReadOnlyList<string> FunctionAutocompleteCandidates { get; private set; } = [];

    private int _functionAutocompleteTokenStart;
    private int _functionAutocompleteTokenLength;
    private bool _suppressNextCellValueAutocomplete;

    public bool IsRangeEntryActive(string? text) =>
        FormulaEditInteractionPlanner.IsRangeEntryActive(text, PointMode);

    public bool IsFormulaText(string? text) =>
        FormulaEditInteractionPlanner.IsFormulaText(text);

    public bool ShouldAppendKeyboardSelection =>
        SelectionMode == ExcelSelectionMode.Add;

    public bool ShouldOfferCellValueAutoComplete(bool enabled) =>
        enabled && !PointMode;

    public bool ShouldCommitInlineArrows(
        string? text,
        bool enteredViaEditKey) =>
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows(
            text,
            PointMode,
            enteredViaEditKey);

    public FormulaEditStatusBarPlan BuildEditStatusBarPlan(bool pointMode) =>
        FormulaEditInteractionPlanner.BuildEditStatusBarPlan(pointMode);

    public bool IsPointModeActive(bool hasRangeEditor, bool hasFormulaEditCell) =>
        hasRangeEditor && PointMode && hasFormulaEditCell;

    public bool TryApplyPointModeSelection(
        FormulaPointModeEditSelection selection,
        bool hasRangeEditor,
        bool hasFormulaEditCell,
        Func<FormulaPointModeEditSelection, bool> appendSelection,
        Func<FormulaPointModeEditSelection, bool> replaceSelection)
    {
        ArgumentNullException.ThrowIfNull(appendSelection);
        ArgumentNullException.ThrowIfNull(replaceSelection);

        if (!IsPointModeActive(hasRangeEditor, hasFormulaEditCell))
            return false;

        return selection.Mode switch
        {
            FormulaPointModeSelectionMode.Append => appendSelection(selection),
            FormulaPointModeSelectionMode.Replace => replaceSelection(selection),
            _ => false,
        };
    }

    public FormulaPointModeCommand? GetRoutedPointModeCommand(
        FormulaEditorKey key,
        bool hasRangeEditor,
        bool hasFormulaEditCell)
    {
        if (IsPointModeActive(hasRangeEditor, hasFormulaEditCell))
            return null;

        return key switch
        {
            FormulaEditorKey.F4 => FormulaPointModeCommand.CycleReference,
            FormulaEditorKey.Escape => FormulaPointModeCommand.Cancel,
            FormulaEditorKey.Enter => FormulaPointModeCommand.Commit,
            _ => null,
        };
    }

    public bool ShouldCycleReference(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        FormulaEditorKey systemKey = FormulaEditorKey.None) =>
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(key, modifiers, systemKey);

    public bool TryPlanReferenceCycle(
        string text,
        int caretIndex,
        CellAddress? anchor,
        bool useR1C1ReferenceStyle,
        out ExcelTextEdit edit) =>
        ExcelTextEditorPlanner.TryCycleFormulaReference(
            text,
            caretIndex,
            anchor,
            useR1C1ReferenceStyle,
            out edit);

    public ExcelEditKeyIntent PlanEditKey(
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        FormulaEditorModifiers modifiers,
        CellAddress current,
        int pageSize,
        string? text,
        bool hasFormulaEditCell,
        FormulaEditorSurfaceKind surface,
        bool enteredViaEditKey,
        bool moveSelectionAfterEnter,
        FormulaEditorEnterDirection enterDirection) =>
        ExcelEditKeyPlanner.GetIntent(
            key,
            modifiers,
            current,
            pageSize,
            allowFormulaBarNavigationKeys:
                surface == FormulaEditorSurfaceKind.FormulaBar && !IsFormulaText(text),
            formulaRangeEntryActive:
                hasFormulaEditCell && IsRangeEntryActive(text),
            inlineEditorCommitsOnArrow:
                surface == FormulaEditorSurfaceKind.Inline &&
                ShouldCommitInlineArrows(text, enteredViaEditKey),
            moveSelectionAfterEnter: moveSelectionAfterEnter,
            enterDirection: enterDirection,
            systemKey: systemKey);

    public void Reset()
    {
        PointMode = false;
        SelectionMode = ExcelSelectionMode.Normal;
        ClearSelection();
        ClearReferenceSpan();
        ClearFunctionAutocomplete();
        _suppressNextCellValueAutocomplete = false;
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

    public bool ClearReferenceSpanIfCaretLeft(
        int textLength,
        int selectionStart,
        int selectionLength,
        int caretIndex,
        bool preserveWhileSelectionActive)
    {
        if (ReferenceSpan is not { } referenceSpan)
            return false;

        var start = referenceSpan.Start;
        var length = referenceSpan.Length;
        var end = start + length;
        if (start < 0 || length < 0 || start > textLength || end > textLength)
        {
            ClearReferenceSpan();
            return true;
        }

        var safeSelectionStart = Math.Clamp(selectionStart, 0, textLength);
        var safeSelectionLength = Math.Clamp(
            selectionLength,
            0,
            textLength - safeSelectionStart);
        if (safeSelectionLength > 0)
        {
            if (preserveWhileSelectionActive)
                return false;

            var selectionEnd = safeSelectionStart + safeSelectionLength;
            if (safeSelectionStart < start || selectionEnd > end)
            {
                ClearReferenceSpan();
                return true;
            }

            return false;
        }

        var caret = Math.Clamp(caretIndex, 0, textLength);
        if (caret >= start && caret <= end)
            return false;

        ClearReferenceSpan();
        return true;
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
        => TryToggleSelectionMode(key, modifiers, out _);

    public bool TryToggleSelectionMode(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        out FormulaRangeSelectionModeChangePlan plan)
    {
        if (!FormulaRangeEntryPlanner.TryToggleKeyboardSelectionMode(
                key,
                modifiers,
                SelectionMode,
                out var next))
        {
            plan = default;
            return false;
        }

        SelectionMode = next;
        plan = next == ExcelSelectionMode.Normal
            ? new FormulaRangeSelectionModeChangePlan(
                next,
                FormulaEditInteractionPlanner.BuildEditStatusBarPlan(pointMode: true),
                StatusBarModeResourceKey: null)
            : new FormulaRangeSelectionModeChangePlan(
                next,
                EditStatusBarPlan: null,
                StatusBarModeResourceKey: ExcelSelectionModePlanner.StatusBarModeResourceKey(next));
        return true;
    }

    public FormulaRangeSelectionPlan PlanSelection(CellAddress target, bool extendSelection)
    {
        var anchor = extendSelection && SelectionAnchor is { } existingAnchor
            ? existingAnchor
            : target;
        var range = PlanRange(anchor, target);

        return new FormulaRangeSelectionPlan(range, anchor, target);
    }

    public GridRange PlanRange(CellAddress anchor, CellAddress target) =>
        new(
            new CellAddress(
                target.Sheet,
                Math.Min(anchor.Row, target.Row),
                Math.Min(anchor.Col, target.Col)),
            new CellAddress(
                target.Sheet,
                Math.Max(anchor.Row, target.Row),
                Math.Max(anchor.Col, target.Col)));

    public FormulaRangeKeyboardNavigationPlan? PlanKeyboardNavigation(
        GridRange selectedRange,
        CellAddress? fallbackCursor,
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        FormulaEditorModifiers modifiers,
        Sheet? sheet,
        int rowPageSize,
        int columnPageSize)
    {
        var current = ResolveKeyboardCursor(selectedRange, fallbackCursor);
        var target = FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
            key,
            systemKey,
            modifiers,
            current,
            sheet,
            rowPageSize,
            columnPageSize);
        if (target is null)
            return null;

        return new FormulaRangeKeyboardNavigationPlan(
            current,
            target.Value,
            ShouldExtendKeyboardSelection(modifiers));
    }

    public CellAddress ResolveKeyboardCursor(
        GridRange selectedRange,
        CellAddress? fallbackCursor) =>
        FormulaRangeEntryPlanner.GetKeyboardCursor(
            selectedRange,
            SelectionCursor ?? fallbackCursor);

    public bool ShouldExtendKeyboardSelection(FormulaEditorModifiers modifiers) =>
        SelectionMode == ExcelSelectionMode.Extend ||
        modifiers.HasFlag(FormulaEditorModifiers.Shift);

    public bool ShouldAppendDisjointReference(FormulaEditorModifiers modifiers) =>
        modifiers.HasFlag(FormulaEditorModifiers.Control) ||
        modifiers.HasFlag(FormulaEditorModifiers.Meta);

    public bool TryPlanRangeSelectionEdit(
        FormulaRangeEditorSnapshot editor,
        GridRange range,
        CellAddress selectionAnchor,
        CellAddress selectionCursor,
        string? replacementText,
        out FormulaRangeSelectionEditPlan plan)
    {
        var previousReferenceSpan = ResolveReferenceSpan(editor);
        var applied = replacementText is not null
            ? FormulaRangeEntryPlanner.TryApplySelectionText(
                editor.Text,
                editor.CaretIndex,
                editor.SelectionLength,
                previousReferenceSpan?.Start,
                previousReferenceSpan?.Length,
                replacementText,
                out var edit)
            : FormulaRangeEntryPlanner.TryApplyRangeSelection(
                editor.Text,
                editor.CaretIndex,
                editor.SelectionLength,
                previousReferenceSpan?.Start,
                previousReferenceSpan?.Length,
                range,
                editor.FormulaCell,
                editor.UseR1C1ReferenceStyle,
                out edit,
                editor.SelectedSheetName,
                SheetSpan,
                editor.SelectedWorkbookName);
        if (!applied)
        {
            plan = default;
            return false;
        }

        plan = new FormulaRangeSelectionEditPlan(
            edit,
            range,
            selectionAnchor,
            selectionCursor,
            UpdateLocalSelection: editor.SelectedWorkbookName is null);
        return true;
    }

    public bool TryPlanDisjointRangeSelectionEdit(
        FormulaRangeEditorSnapshot editor,
        GridRange range,
        CellAddress selectionAnchor,
        CellAddress selectionCursor,
        bool includeSheetSpan,
        out FormulaRangeSelectionEditPlan plan)
    {
        var previousReferenceSpan = ResolveReferenceSpan(editor);
        if (previousReferenceSpan is null ||
            !FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                editor.Text,
                previousReferenceSpan.Value.Start,
                previousReferenceSpan.Value.Length,
                range,
                editor.FormulaCell,
                editor.UseR1C1ReferenceStyle,
                out var edit,
                editor.SelectedSheetName,
                includeSheetSpan ? SheetSpan : null,
                editor.SelectedWorkbookName))
        {
            plan = default;
            return false;
        }

        plan = new FormulaRangeSelectionEditPlan(
            edit,
            range,
            selectionAnchor,
            selectionCursor,
            UpdateLocalSelection: editor.SelectedWorkbookName is null);
        return true;
    }

    public bool TryPlanKeyboardDisjointRangeSelectionEdit(
        FormulaRangeEditorSnapshot editor,
        CellAddress current,
        CellAddress target,
        bool extendSelection,
        out FormulaRangeSelectionEditPlan plan)
    {
        var range = PlanKeyboardSelectionRange(current, target, extendSelection);
        return TryPlanDisjointRangeSelectionEdit(
            editor,
            range,
            range.Start,
            range.End,
            includeSheetSpan: true,
            out plan);
    }

    public GridRange PlanKeyboardSelectionRange(
        CellAddress current,
        CellAddress target,
        bool extendSelection) =>
        FormulaRangeEntryPlanner.GetKeyboardDisjointRange(
            current,
            target,
            extendSelection);

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

    public void ApplySelectionEdit(FormulaRangeSelectionEditPlan plan) =>
        ApplyPlannerEdit(plan.Edit, plan.Anchor, plan.Cursor);

    public bool TryBeginReferenceDrag(FormulaReferenceHighlight highlight)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        if (highlight.Range is null)
            return false;

        ReferenceDragHighlight = highlight;
        return true;
    }

    public GridRange? PlanActiveReferenceDrag(CellAddress target) =>
        ReferenceDragHighlight is { } highlight
            ? PlanReferenceDrag(highlight, target)
            : null;

    public GridRange? PlanReferenceDrag(
        FormulaReferenceHighlight highlight,
        CellAddress target)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        return highlight.Range is { } originalRange &&
            originalRange.Start.Sheet == target.Sheet
                ? FormulaReferenceDragResizePlanner.ComputeResizedRange(
                    originalRange.Start,
                    target)
                : null;
    }

    public FormulaReferenceHighlight? EndReferenceDrag()
    {
        var highlight = ReferenceDragHighlight;
        ReferenceDragHighlight = null;
        return highlight;
    }

    public void CancelReferenceDrag() => ReferenceDragHighlight = null;

    public ExcelTextEdit PlanReferenceResizeEdit(
        string text,
        FormulaReferenceHighlight highlight,
        GridRange range,
        bool useR1C1ReferenceStyle)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        var (newText, caretIndex) = FormulaReferenceDragResizePlanner.ApplyResize(
            text,
            highlight.TextStart,
            highlight.TextLength,
            range,
            useR1C1ReferenceStyle);
        return new ExcelTextEdit(newText, caretIndex, 0);
    }

    public void ApplyReferenceResizeEdit(
        FormulaReferenceHighlight highlight,
        ExcelTextEdit edit)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        TrackReferenceSpan(
            highlight.TextStart,
            edit.SelectionStart - highlight.TextStart);
    }

    public IReadOnlyList<string> RefreshFunctionAutocomplete(
        string? text,
        int caretIndex,
        IEnumerable<string>? functionNames,
        IEnumerable<string>? definedNames,
        IEnumerable<string>? tableNames)
    {
        if (!FormulaFunctionAutocompletePlanner.ShouldShowAutocomplete(
                text,
                caretIndex,
                out var tokenStart,
                out var tokenLength,
                out var prefix))
        {
            ClearFunctionAutocomplete();
            return FunctionAutocompleteCandidates;
        }

        var candidates = FormulaFunctionAutocompletePlanner.BuildCandidates(
            prefix,
            functionNames,
            definedNames,
            tableNames);
        if (candidates.Count == 0)
        {
            ClearFunctionAutocomplete();
            return FunctionAutocompleteCandidates;
        }

        _functionAutocompleteTokenStart = tokenStart;
        _functionAutocompleteTokenLength = tokenLength;
        FunctionAutocompleteCandidates = candidates;
        return FunctionAutocompleteCandidates;
    }

    public int MoveFunctionAutocompleteSelection(int currentIndex, int delta) =>
        FormulaFunctionAutocompletePlanner.MoveSelection(
            currentIndex,
            FunctionAutocompleteCandidates.Count,
            delta);

    public ExcelTextEdit CommitFunctionAutocomplete(
        string text,
        string chosenName,
        IEnumerable<string>? functionNames)
    {
        var isFunction = FormulaFunctionAutocompletePlanner.IsFunctionCandidate(
            chosenName,
            functionNames);
        var (updatedText, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            text,
            _functionAutocompleteTokenStart,
            _functionAutocompleteTokenLength,
            chosenName,
            isFunction);
        ClearFunctionAutocomplete();
        return new ExcelTextEdit(updatedText, caretIndex, 0);
    }

    public void ClearFunctionAutocomplete()
    {
        FunctionAutocompleteCandidates = [];
        _functionAutocompleteTokenStart = 0;
        _functionAutocompleteTokenLength = 0;
    }

    public void SuppressNextCellValueAutocomplete() =>
        _suppressNextCellValueAutocomplete = true;

    public bool ConsumeCellValueAutocompleteSuppression()
    {
        var suppressed = _suppressNextCellValueAutocomplete;
        _suppressNextCellValueAutocomplete = false;
        return suppressed;
    }

    public FormulaCellValueAutocompletePlan? PlanCellValueAutocomplete(
        bool enabled,
        string? text,
        int caretIndex,
        int selectionLength,
        Sheet? sheet,
        CellAddress address)
    {
        if (!ShouldOfferCellValueAutoComplete(enabled) ||
            string.IsNullOrEmpty(text) ||
            IsFormulaText(text) ||
            selectionLength != 0 ||
            caretIndex != text.Length ||
            sheet is null)
        {
            return null;
        }

        var candidates = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet,
            address);
        var suggestion = CellValueAutoCompleteSuggester.Suggest(candidates, text);
        return suggestion is null
            ? null
            : new FormulaCellValueAutocompletePlan(
                suggestion,
                SelectionStart: text.Length,
                SelectionLength: suggestion.Length - text.Length);
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

    private FormulaReferenceEntrySpan? ResolveReferenceSpan(FormulaRangeEditorSnapshot editor)
    {
        if (!FormulaRangeEntryPlanner.TryGetReferenceSpanForPointEntry(
                editor.Text,
                ReferenceSpan?.Start,
                ReferenceSpan?.Length,
                editor.CaretIndex,
                editor.SelectionLength,
                out var referenceStart,
                out var referenceLength))
        {
            return null;
        }

        return new FormulaReferenceEntrySpan(referenceStart, referenceLength);
    }
}
