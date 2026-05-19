using Freexcel.Core.Commands;

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
    {
        edit = new ExcelTextEdit(text, Math.Clamp(caretIndex, 0, text.Length), 0);
        if (!text.StartsWith("=", StringComparison.Ordinal))
            return false;

        if (!FormulaReferenceCycler.TryCycleReferenceAtCaret(
                text,
                caretIndex,
                out var cycled,
                out var selectionStart,
                out var selectionLength))
            return false;

        edit = new ExcelTextEdit(cycled, selectionStart, selectionLength);
        return true;
    }
}
