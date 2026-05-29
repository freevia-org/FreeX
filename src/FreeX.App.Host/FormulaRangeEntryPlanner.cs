using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record FormulaRangeEntryEdit(
    ExcelTextEdit TextEdit,
    int ReferenceStart,
    int ReferenceLength);

public static class FormulaRangeEntryPlanner
{
    public static CellAddress GetKeyboardCursor(GridRange selectedRange, CellAddress? selectionCursor)
        => selectionCursor is { } cursor && cursor.Sheet == selectedRange.Start.Sheet
            ? cursor
            : selectedRange.Start;

    public static CellAddress? GetKeyboardSelectionTarget(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        CellAddress current,
        Sheet? sheet,
        int rowPageSize,
        int colPageSize)
    {
        var horizontalPageTarget = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            key,
            systemKey,
            modifiers,
            current,
            colPageSize);
        if (horizontalPageTarget is { })
            return horizontalPageTarget;

        if ((modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            return null;

        var effectiveKey = key is Key.None or Key.System ? systemKey : key;
        var useDataBoundary = ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(effectiveKey, modifiers, endMode: false);
        var ctrlHeld = (modifiers & ModifierKeys.Control) != 0;

        return effectiveKey switch
        {
            Key.Up => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1)
                : new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, +1)
                : new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
            Key.Left => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                : new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                : new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            Key.Home => new CellAddress(current.Sheet, ctrlHeld ? 1u : current.Row, 1u),
            Key.End => ctrlHeld
                ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, current.Sheet)
                : null,
            Key.PageUp => new CellAddress(current.Sheet, (uint)Math.Max(1, (int)current.Row - rowPageSize), current.Col),
            Key.PageDown => new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + (uint)rowPageSize), current.Col),
            _ => null
        };
    }

    public static bool TryApplyRangeSelection(
        string text,
        int caretIndex,
        int selectionLength,
        int? previousReferenceStart,
        int? previousReferenceLength,
        GridRange selectedRange,
        CellAddress formulaCell,
        bool useR1C1ReferenceStyle,
        out FormulaRangeEntryEdit edit)
    {
        var referenceText = SpreadsheetDisplayFormatter.FormatRangeReference(
            selectedRange.Start,
            selectedRange.End,
            useR1C1ReferenceStyle);

        return TryApplySelectionText(
            text,
            caretIndex,
            selectionLength,
            previousReferenceStart,
            previousReferenceLength,
            referenceText,
            out edit);
    }

    public static bool TryApplySelectionText(
        string text,
        int caretIndex,
        int selectionLength,
        int? previousReferenceStart,
        int? previousReferenceLength,
        string selectionText,
        out FormulaRangeEntryEdit edit)
    {
        var safeCaret = Math.Clamp(caretIndex, 0, text.Length);
        edit = new FormulaRangeEntryEdit(new ExcelTextEdit(text, safeCaret, 0), safeCaret, 0);

        if (!text.StartsWith("=", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(selectionText))
            return false;

        var replacementStart = safeCaret;
        var replacementLength = Math.Clamp(selectionLength, 0, text.Length - replacementStart);

        if (previousReferenceStart is { } previousStart &&
            previousReferenceLength is { } previousLength &&
            previousStart >= 1 &&
            previousStart <= text.Length &&
            previousLength >= 0 &&
            previousStart + previousLength <= text.Length &&
            safeCaret >= previousStart &&
            safeCaret <= previousStart + previousLength)
        {
            replacementStart = previousStart;
            replacementLength = previousLength;
        }

        var updated = text
            .Remove(replacementStart, replacementLength)
            .Insert(replacementStart, selectionText);

        edit = new FormulaRangeEntryEdit(
            new ExcelTextEdit(updated, replacementStart + selectionText.Length, 0),
            replacementStart,
            selectionText.Length);
        return true;
    }
}
