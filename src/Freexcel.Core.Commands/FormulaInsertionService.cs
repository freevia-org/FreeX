namespace Freexcel.Core.Commands;

public sealed record FormulaInsertionResult(string Text, int CaretIndex);

public static class FormulaInsertionService
{
    public static FormulaInsertionResult InsertDefinedName(string? formulaText, int caretIndex, string name)
    {
        var text = formulaText ?? "";
        if (!text.StartsWith('='))
        {
            text = "=" + text;
            caretIndex++;
        }

        caretIndex = Math.Clamp(caretIndex, 1, text.Length);
        var result = text.Insert(caretIndex, name);
        return new FormulaInsertionResult(result, caretIndex + name.Length);
    }
}
