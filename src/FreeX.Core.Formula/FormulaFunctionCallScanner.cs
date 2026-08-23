namespace FreeX.Core.Formula;

internal static class FormulaFunctionCallScanner
{
    internal static bool ContainsSubtotalOrAggregateCall(string? formulaText)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        return ContainsFunctionCall(formulaText, "SUBTOTAL") ||
               ContainsFunctionCall(formulaText, "AGGREGATE");
    }

    private static bool ContainsFunctionCall(string text, string functionName)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString || i + functionName.Length > text.Length ||
                string.Compare(
                    text,
                    i,
                    functionName,
                    0,
                    functionName.Length,
                    StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            var precededByIdentifierCharacter = i > 0 &&
                (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.');
            if (precededByIdentifierCharacter)
                continue;

            var cursor = i + functionName.Length;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                cursor++;

            if (cursor < text.Length && text[cursor] == '(')
                return true;
        }

        return false;
    }
}
