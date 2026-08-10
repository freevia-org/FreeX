using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

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

    public static bool TryToggleKeyboardSelectionMode(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        ExcelSelectionMode current,
        out ExcelSelectionMode next)
    {
        next = current;
        if (key != FormulaEditorKey.F8)
            return false;

        if (modifiers == FormulaEditorModifiers.None)
        {
            next = current == ExcelSelectionMode.Extend
                ? ExcelSelectionMode.Normal
                : ExcelSelectionMode.Extend;
            return true;
        }

        if (modifiers == FormulaEditorModifiers.Shift)
        {
            next = current == ExcelSelectionMode.Add
                ? ExcelSelectionMode.Normal
                : ExcelSelectionMode.Add;
            return true;
        }

        return false;
    }

    public static GridRange GetKeyboardDisjointRange(
        CellAddress current,
        CellAddress target,
        bool extendSelection)
    {
        if (!extendSelection || current.Sheet != target.Sheet)
            return new GridRange(target, target);

        return new GridRange(
            new CellAddress(target.Sheet, Math.Min(current.Row, target.Row), Math.Min(current.Col, target.Col)),
            new CellAddress(target.Sheet, Math.Max(current.Row, target.Row), Math.Max(current.Col, target.Col)));
    }

    public static bool TryAppendKeyboardRangeSelection(
        string text,
        int? previousReferenceStart,
        int? previousReferenceLength,
        CellAddress current,
        CellAddress target,
        bool extendSelection,
        CellAddress formulaCell,
        bool useR1C1ReferenceStyle,
        out FormulaRangeEntryEdit edit,
        string? selectedSheetName = null,
        FormulaSheetSpanEntryState? sheetSpan = null)
    {
        var range = GetKeyboardDisjointRange(current, target, extendSelection);
        return TryAppendDisjointRangeSelection(
            text,
            previousReferenceStart,
            previousReferenceLength,
            range,
            formulaCell,
            useR1C1ReferenceStyle,
            out edit,
            selectedSheetName,
            sheetSpan);
    }

    public static CellAddress? GetKeyboardSelectionTarget(
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        FormulaEditorModifiers modifiers,
        CellAddress current,
        Sheet? sheet,
        int rowPageSize,
        int colPageSize)
    {
        var horizontalPageTarget = GetHorizontalPageTarget(
            key,
            systemKey,
            modifiers,
            current,
            colPageSize);
        if (horizontalPageTarget is { })
            return horizontalPageTarget;

        if ((modifiers & ~(FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift)) != 0)
            return null;

        var effectiveKey = key is FormulaEditorKey.None or FormulaEditorKey.System ? systemKey : key;
        var useDataBoundary = ShouldUseDataBoundary(effectiveKey, modifiers, endMode: false);
        var ctrlHeld = (modifiers & FormulaEditorModifiers.Control) != 0;

        return effectiveKey switch
        {
            FormulaEditorKey.Up => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1)
                : new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            FormulaEditorKey.Down => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, +1)
                : new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
            FormulaEditorKey.Left => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                : new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            FormulaEditorKey.Right => useDataBoundary
                ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                : new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            FormulaEditorKey.Home => new CellAddress(current.Sheet, ctrlHeld ? 1u : current.Row, 1u),
            FormulaEditorKey.End => ctrlHeld
                ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, current.Sheet)
                : null,
            FormulaEditorKey.PageUp => new CellAddress(current.Sheet, (uint)Math.Max(1, (int)current.Row - rowPageSize), current.Col),
            FormulaEditorKey.PageDown => new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + (uint)rowPageSize), current.Col),
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
        out FormulaRangeEntryEdit edit,
        string? selectedSheetName = null,
        FormulaSheetSpanEntryState? sheetSpan = null,
        string? selectedWorkbookName = null)
    {
        var referenceText = FormatRangeReference(
            selectedRange,
            formulaCell,
            useR1C1ReferenceStyle,
            selectedSheetName,
            sheetSpan,
            selectedWorkbookName);

        return TryApplySelectionText(
            text,
            caretIndex,
            selectionLength,
            previousReferenceStart,
            previousReferenceLength,
            referenceText,
            out edit);
    }

    public static bool TryAppendDisjointRangeSelection(
        string text,
        int? previousReferenceStart,
        int? previousReferenceLength,
        GridRange selectedRange,
        CellAddress formulaCell,
        bool useR1C1ReferenceStyle,
        out FormulaRangeEntryEdit edit,
        string? selectedSheetName = null,
        FormulaSheetSpanEntryState? sheetSpan = null,
        string? selectedWorkbookName = null)
    {
        var safeCaret = text.Length;
        edit = new FormulaRangeEntryEdit(new ExcelTextEdit(text, safeCaret, 0), safeCaret, 0);

        if (previousReferenceStart is not { } start ||
            previousReferenceLength is not { } length ||
            start < 0 ||
            length < 0 ||
            start + length > text.Length)
        {
            return false;
        }

        var referenceText = FormatRangeReference(
            selectedRange,
            formulaCell,
            useR1C1ReferenceStyle,
            selectedSheetName,
            sheetSpan,
            selectedWorkbookName);
        var insertAt = start + length;
        var insertionText = "," + referenceText;
        var updatedText = text.Insert(insertAt, insertionText);

        edit = new FormulaRangeEntryEdit(
            new ExcelTextEdit(updatedText, insertAt + insertionText.Length, 0),
            insertAt + 1,
            referenceText.Length);
        return true;
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

    // Avalonia can briefly lose the tracked selection span when a physical X11 point click
    // returns focus to the formula editor. Recover the trailing reference from the editor caret
    // so the production append path still inserts the required comma separator.
    public static bool TryGetTrailingReferenceSpan(
        string text,
        int caretIndex,
        out int referenceStart,
        out int referenceLength)
    {
        referenceStart = 0;
        referenceLength = 0;
        var end = Math.Clamp(caretIndex, 0, text.Length);
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
            end--;

        if (end == 0)
            return false;

        var inSheetName = false;
        var start = end;
        for (var index = end - 1; index >= 0; index--)
        {
            var character = text[index];
            if (character == '\'')
            {
                inSheetName = !inSheetName;
                continue;
            }

            if (inSheetName)
                continue;

            if (character is '(' or ',' or '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>')
            {
                start = index + 1;
                break;
            }

            start = index;
        }

        while (start < end && char.IsWhiteSpace(text[start]))
            start++;

        referenceStart = start;
        referenceLength = end - start;
        return referenceLength > 0;
    }

    public static bool TryGetReferenceSpanForPointEntry(
        string text,
        int? trackedReferenceStart,
        int? trackedReferenceLength,
        int caretIndex,
        int selectionLength,
        out int referenceStart,
        out int referenceLength)
    {
        referenceStart = 0;
        referenceLength = 0;

        if (trackedReferenceStart is { } trackedStart &&
            trackedReferenceLength is { } trackedLength &&
            trackedStart >= 0 && trackedLength >= 0 &&
            trackedStart + trackedLength <= text.Length)
        {
            referenceStart = trackedStart;
            referenceLength = trackedLength;
            return true;
        }

        return selectionLength == 0 &&
            TryGetTrailingReferenceSpan(text, caretIndex, out referenceStart, out referenceLength);
    }

    private static string FormatRangeReference(
        GridRange selectedRange,
        CellAddress formulaCell,
        bool useR1C1ReferenceStyle,
        string? selectedSheetName,
        FormulaSheetSpanEntryState? sheetSpan,
        string? selectedWorkbookName)
    {
        var shorthand = useR1C1ReferenceStyle
            ? null
            : FormatWholeRowOrColumnReferenceShorthand(selectedRange);
        var cellReferenceText = shorthand
                ?? SpreadsheetDisplayFormatter.FormatRangeReference(
                    selectedRange.Start,
                    selectedRange.End,
                    useR1C1ReferenceStyle);
        if (sheetSpan is { HasSpan: true })
            return $"{FormulaSheetSpanEntryPlanner.FormatSheetQualifier(sheetSpan.Value)}!{cellReferenceText}";

        if (selectedWorkbookName is not null && selectedSheetName is not null)
        {
            var externalSheetName = $"[{selectedWorkbookName}]{selectedSheetName}";
            return $"{SheetNameFormatter.QuoteIfNeeded(externalSheetName)}!{cellReferenceText}";
        }

        return selectedRange.Start.Sheet == formulaCell.Sheet || selectedSheetName is null
            ? cellReferenceText
            : $"{SheetNameFormatter.QuoteIfNeeded(selectedSheetName)}!{cellReferenceText}";
    }

    public static string? FormatWholeRowOrColumnReferenceShorthand(GridRange range)
    {
        var isWholeColumnBand = range.Start.Row == 1 && range.End.Row == CellAddress.MaxRow;
        var isWholeRowBand = range.Start.Col == 1 && range.End.Col == CellAddress.MaxCol;

        // A whole-sheet selection has no bare Excel shorthand; retain its full A1 extent.
        if (isWholeColumnBand == isWholeRowBand)
            return null;

        if (isWholeColumnBand)
        {
            var firstColumn = FormatColumnReference(range.Start.Col);
            var lastColumn = FormatColumnReference(range.End.Col);
            return firstColumn == lastColumn
                ? $"{firstColumn}:{firstColumn}"
                : $"{firstColumn}:{lastColumn}";
        }

        return range.Start.Row == range.End.Row
            ? $"{range.Start.Row}:{range.Start.Row}"
            : $"{range.Start.Row}:{range.End.Row}";
    }

    private static string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, useR1C1ReferenceStyle: false);

    private static CellAddress? GetHorizontalPageTarget(
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        FormulaEditorModifiers modifiers,
        CellAddress current,
        int pageSize)
    {
        if (modifiers is not FormulaEditorModifiers.Alt and not (FormulaEditorModifiers.Alt | FormulaEditorModifiers.Shift))
            return null;

        var effectiveKey = key is FormulaEditorKey.None or FormulaEditorKey.System ? systemKey : key;
        return effectiveKey switch
        {
            FormulaEditorKey.PageDown => new CellAddress(
                current.Sheet,
                current.Row,
                Math.Min(current.Col + (uint)Math.Max(1, pageSize), CellAddress.MaxCol)),
            FormulaEditorKey.PageUp => new CellAddress(
                current.Sheet,
                current.Row,
                (uint)Math.Max(1, (int)current.Col - Math.Max(1, pageSize))),
            _ => null
        };
    }

    private static bool ShouldUseDataBoundary(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        bool endMode) =>
        key is FormulaEditorKey.Up or FormulaEditorKey.Down or FormulaEditorKey.Left or FormulaEditorKey.Right &&
        (endMode
            ? modifiers is FormulaEditorModifiers.None or FormulaEditorModifiers.Shift
            : modifiers is FormulaEditorModifiers.Control or
                (FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift));

}
