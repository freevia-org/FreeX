using FreeX.App.Presentation;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

public enum FormulaKeyboardSelectionRoute
{
    None,
    Direct,
    DisjointReference,
    RangeFallback,
}

public readonly record struct FormulaKeyboardSelectionResult(
    FormulaKeyboardSelectionRoute Route,
    GridRange Range)
{
    public bool Applied => Route != FormulaKeyboardSelectionRoute.None;
}

/// <summary>
/// Coordinates formula-reference editing decisions that are common to every renderer. Native
/// editors, selection controls, focus, and highlight visuals are supplied by the host callbacks.
/// </summary>
public static class FormulaReferenceEditingController
{
    public static void Reset(
        FormulaRangeEditingSession session,
        Action hideAutocomplete,
        Action clearRendererHighlights)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(hideAutocomplete);
        ArgumentNullException.ThrowIfNull(clearRendererHighlights);

        hideAutocomplete();
        session.Reset();
        clearRendererHighlights();
    }

    public static bool TryApplyKeyboardSelection(
        FormulaRangeEditingSession session,
        CellAddress current,
        CellAddress target,
        bool extendSelection,
        FormulaRangeEditorSnapshot? editor,
        Func<CellAddress, bool, bool> applyDirectSelection,
        Action<ExcelTextEdit> applyEditorEdit,
        Action<FormulaRangeSelectionEditPlan>? afterEditorEdit,
        Func<GridRange, CellAddress, CellAddress, bool> applyRangeFallback,
        out FormulaKeyboardSelectionResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applyDirectSelection);
        ArgumentNullException.ThrowIfNull(applyEditorEdit);
        ArgumentNullException.ThrowIfNull(applyRangeFallback);

        var range = session.PlanKeyboardSelectionRange(current, target, extendSelection);
        if (!session.ShouldAppendKeyboardSelection)
        {
            var applied = applyDirectSelection(target, extendSelection);
            result = new FormulaKeyboardSelectionResult(
                applied ? FormulaKeyboardSelectionRoute.Direct : FormulaKeyboardSelectionRoute.None,
                range);
            return applied;
        }

        if (editor is null)
        {
            result = new FormulaKeyboardSelectionResult(FormulaKeyboardSelectionRoute.None, range);
            return false;
        }

        if (session.TryApplyKeyboardDisjointRangeSelectionEdit(
                editor.Value,
                current,
                target,
                extendSelection,
                applyEditorEdit,
                afterEditorEdit,
                out _))
        {
            result = new FormulaKeyboardSelectionResult(
                FormulaKeyboardSelectionRoute.DisjointReference,
                range);
            return true;
        }

        var fallbackApplied = applyRangeFallback(range, range.Start, target);
        result = new FormulaKeyboardSelectionResult(
            fallbackApplied ? FormulaKeyboardSelectionRoute.RangeFallback : FormulaKeyboardSelectionRoute.None,
            range);
        return fallbackApplied;
    }

    public static IReadOnlyList<FormulaReferenceHighlight> BuildHighlights(
        string? text,
        Workbook workbook,
        SheetId currentSheetId,
        CellAddress? formulaCell)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return FormulaReferenceHighlightPlanner.GetHighlights(
            text ?? string.Empty,
            currentSheetId,
            sheetName => workbook.GetSheet(sheetName)?.Id,
            (tableName, selector) => StructuredReferenceResolver.ResolveEditorReference(
                workbook,
                workbook.GetSheet(currentSheetId),
                formulaCell,
                tableName,
                selector),
            sheetId => FindSheetIndex(workbook, sheetId));
    }

    private static int? FindSheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            if (workbook.Sheets[index].Id == sheetId)
                return index;
        }

        return null;
    }
}
