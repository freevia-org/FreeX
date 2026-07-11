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

        // Real DV formulas are stored per OOXML convention with NO leading '=' (see
        // XlsxDataValidationClosedXmlMapper.Load, DataValidationBoundsParser.TryEvaluateBoundFormula,
        // and DataValidationService.ValidateCustom, which all defensively prepend '=' for this reason).
        // Track whether the caller's text carried an explicit '=' so we can restore the exact storage
        // form afterwards, but don't let its absence block the rewrite.
        var trimmed = formula.TrimStart();
        var hasLeadingEquals = trimmed.StartsWith('=');
        var expression = hasLeadingEquals ? trimmed[1..] : trimmed;

        if (!LooksLikeCellReferenceFormula(expression))
            return formula;

        // A comma is just as likely to be a function-argument separator (e.g. AND(A1>0,B1>0)) as an
        // inline list-literal separator (e.g. "1,2,3") -- FormulaRewriter is a full AST rewriter and
        // handles the former fine, while the latter simply fails to parse as an expression and falls
        // through to the unchanged-formula return below.
        var rewritten = FormulaRewriter.Rewrite(expression, new PasteOffsetOp(rowDelta, colDelta), hostSheetName);
        if (rewritten is null)
            return formula;

        return hasLeadingEquals ? "=" + rewritten : rewritten;
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
