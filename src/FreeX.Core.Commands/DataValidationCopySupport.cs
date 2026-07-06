using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DataValidationCopySupport
{
    public static DataValidation CloneValidation(DataValidation source) =>
        CloneValidation(source, source.AppliesTo);

    public static DataValidation CloneValidation(DataValidation source, GridRange range) =>
        CloneValidation(source, range, hostSheetName: null, rowDelta: 0, colDelta: 0);

    public static DataValidation CloneValidation(
        DataValidation source,
        GridRange range,
        string? hostSheetName,
        int rowDelta,
        int colDelta,
        bool includeAdditionalRanges = true)
    {
        var clone = new DataValidation
        {
            AppliesTo = range,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = RewriteValidationFormula(source.Formula1, hostSheetName, rowDelta, colDelta),
            Formula2 = RewriteValidationFormula(source.Formula2, hostSheetName, rowDelta, colDelta),
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage,
            IsX14 = source.IsX14,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };

        if (includeAdditionalRanges)
            clone.AdditionalRanges.AddRange(source.AdditionalRanges);

        return clone;
    }

    private static string? RewriteValidationFormula(string? formula, string? hostSheetName, int rowDelta, int colDelta)
    {
        if (string.IsNullOrWhiteSpace(formula) || hostSheetName is null || (rowDelta == 0 && colDelta == 0))
            return formula;

        var trimmed = formula.TrimStart();
        if (!trimmed.StartsWith('=') || trimmed.Contains(','))
            return formula;

        var expression = trimmed[1..];
        if (!LooksLikeCellReferenceFormula(expression))
            return formula;

        var rewritten = FormulaRewriter.Rewrite(expression, new PasteOffsetOp(rowDelta, colDelta), hostSheetName);
        return rewritten is null ? formula : "=" + rewritten;
    }

    private static bool LooksLikeCellReferenceFormula(string expression)
    {
        foreach (var ch in expression)
        {
            if (char.IsDigit(ch) || ch is '$' or '!' or ':')
                return true;
        }

        return false;
    }
}
