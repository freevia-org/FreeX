using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.FormulaBar;

/// <summary>
/// One argument in a live signature tooltip, with whether it is the argument the caret currently
/// sits inside (rendered bold in Excel's own ScreenTip).
/// </summary>
public readonly record struct FormulaSignatureArgument(string Name, bool Optional, bool IsCurrent);

/// <summary>
/// The resolved signature-help state for the function call enclosing the caret: the function's
/// name and its ordered argument list, with the current argument flagged.
/// </summary>
public sealed record FormulaSignatureHelpInfo(
    string FunctionName,
    IReadOnlyList<FormulaSignatureArgument> Arguments);

/// <summary>
/// Portable, UI-free planner behind Excel's live function-signature ScreenTip: while the caret sits
/// inside an open function call (after typing e.g. <c>=VLOOKUP(</c>), resolves which function call
/// encloses the caret and which argument position the caret is currently in, so a thin shell-side
/// tooltip can render "VLOOKUP(lookup_value, table_array, **col_index_num**, [range_lookup])" with
/// the live argument bolded. Re-bolds as the user types past each comma.
/// </summary>
/// <remarks>
/// Per-function argument names/optionality already live in <see cref="FunctionArgumentCatalog"/>
/// (used by the Insert Function / Function Arguments dialogs); this planner adds only the "which
/// call, which argument" resolution driven by caret position, kept dependency-free of any UI type so
/// it is directly unit-testable and reusable from either shell's formula editor.
/// </remarks>
public static class FormulaSignatureHelpPlanner
{
    /// <summary>
    /// Resolves the signature-help info for the caret position, or null when the caret is not inside
    /// any function call's argument list (e.g. before the first "=", inside a plain grouping
    /// parenthesis with no preceding identifier, or past the call's closing parenthesis).
    /// </summary>
    public static FormulaSignatureHelpInfo? Resolve(string? text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("=", StringComparison.Ordinal))
            return null;

        var clampedCaret = Math.Clamp(caretIndex, 0, text.Length);
        if (!TryFindEnclosingCall(text, clampedCaret, out var openParenIndex, out var currentArgumentIndex))
            return null;

        if (!TryReadFunctionName(text, openParenIndex, out var nameStart, out var nameLength))
            return null;

        var functionName = text.Substring(nameStart, nameLength).ToUpperInvariant();
        var specs = FunctionArgumentCatalog.GetArgumentSpecs(functionName);
        if (specs.Count == 0)
            return new FormulaSignatureHelpInfo(functionName, []);

        var highlightedIndex = Math.Min(currentArgumentIndex, specs.Count - 1);
        var arguments = new FormulaSignatureArgument[specs.Count];
        for (var i = 0; i < specs.Count; i++)
            arguments[i] = new FormulaSignatureArgument(specs[i].Name, specs[i].Optional, i == highlightedIndex);

        return new FormulaSignatureHelpInfo(functionName, arguments);
    }

    /// <summary>
    /// Scans backward from the caret tracking parenthesis depth (skipping over quoted string
    /// literals) to find the nearest enclosing unmatched "(" and the number of depth-0 commas
    /// between it and the caret (the current 0-based argument index).
    /// </summary>
    private static bool TryFindEnclosingCall(string text, int caretIndex, out int openParenIndex, out int argumentIndex)
    {
        openParenIndex = -1;
        argumentIndex = 0;
        var depth = 0;

        var i = caretIndex - 1;
        while (i >= 0)
        {
            var c = text[i];
            if (c == '"')
            {
                i = SkipStringLiteralBackward(text, i);
                continue;
            }

            if (c == ')')
            {
                depth++;
            }
            else if (c == '(')
            {
                if (depth == 0)
                {
                    openParenIndex = i;
                    return true;
                }

                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                argumentIndex++;
            }

            i--;
        }

        return false;
    }

    /// <summary>
    /// Given the index of a closing double-quote, returns the index of the matching opening quote
    /// scanning backward (Excel formula strings escape an embedded quote as "" -- a run of an even
    /// number of quotes just before this one is still inside the same literal).
    /// </summary>
    private static int SkipStringLiteralBackward(string text, int closingQuoteIndex)
    {
        var i = closingQuoteIndex - 1;
        while (i >= 0 && text[i] != '"')
            i--;
        return i - 1;
    }

    private static bool TryReadFunctionName(string text, int openParenIndex, out int nameStart, out int nameLength)
    {
        nameStart = openParenIndex;
        nameLength = 0;

        var i = openParenIndex;
        while (i > 0 && IsFunctionNameChar(text[i - 1]))
            i--;

        if (i == openParenIndex)
            return false;

        nameStart = i;
        nameLength = openParenIndex - i;
        return true;
    }

    private static bool IsFunctionNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '.';
}
