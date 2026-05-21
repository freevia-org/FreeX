using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        var safeCaret = Math.Clamp(caretIndex, 0, text.Length);
        edit = new FormulaRangeEntryEdit(new ExcelTextEdit(text, safeCaret, 0), safeCaret, 0);

        if (!text.StartsWith("=", StringComparison.Ordinal))
            return false;

        var referenceText = SpreadsheetDisplayFormatter.FormatRangeReference(
            selectedRange.Start,
            selectedRange.End,
            useR1C1ReferenceStyle);

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
            .Insert(replacementStart, referenceText);

        edit = new FormulaRangeEntryEdit(
            new ExcelTextEdit(updated, replacementStart + referenceText.Length, 0),
            replacementStart,
            referenceText.Length);
        return true;
    }
}
