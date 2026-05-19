using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ExcelTextEdit(string Text, int SelectionStart, int SelectionLength);

public static class ExcelTextEditorPlanner
{
    public static ExcelTextEdit InsertLineBreak(
        string text,
        int selectionStart,
        int selectionLength,
        string newLine)
    {
        var safeStart = Math.Clamp(selectionStart, 0, text.Length);
        var safeLength = Math.Clamp(selectionLength, 0, text.Length - safeStart);
        var updated = text.Remove(safeStart, safeLength).Insert(safeStart, newLine);
        return new ExcelTextEdit(updated, safeStart + newLine.Length, 0);
    }

    public static bool TryCycleFormulaReference(string text, int caretIndex, out ExcelTextEdit edit)
        => TryCycleFormulaReference(text, caretIndex, anchor: null, useR1C1ReferenceStyle: false, out edit);

    public static bool TryCycleFormulaReference(
        string text,
        int caretIndex,
        CellAddress? anchor,
        bool useR1C1ReferenceStyle,
        out ExcelTextEdit edit)
    {
        edit = new ExcelTextEdit(text, Math.Clamp(caretIndex, 0, text.Length), 0);
        if (!text.StartsWith("=", StringComparison.Ordinal))
            return false;

        var cycled = string.Empty;
        var selectionStart = 0;
        var selectionLength = 0;
        var changed = useR1C1ReferenceStyle && anchor is { } address
            ? FormulaReferenceCycler.TryCycleR1C1ReferenceAtCaret(
                text,
                caretIndex,
                address,
                out cycled,
                out selectionStart,
                out selectionLength)
            : FormulaReferenceCycler.TryCycleReferenceAtCaret(
                text,
                caretIndex,
                out cycled,
                out selectionStart,
                out selectionLength);

        if (!changed)
            return false;

        edit = new ExcelTextEdit(cycled, selectionStart, selectionLength);
        return true;
    }
}
