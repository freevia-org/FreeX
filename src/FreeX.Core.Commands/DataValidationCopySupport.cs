using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DataValidationCopySupport
{
    public static DataValidation CloneValidation(DataValidation source) =>
        CloneValidation(source, source.AppliesTo);

    public static DataValidation CloneValidation(DataValidation source, GridRange range) =>
        CloneValidation(source, range, hostSheetName: null, pasteOp: null);

    // Back-compat overload for callers that only ever perform a uniform per-cell translation
    // (Format Painter, the Clear/Set-validation subtract-and-replace loops) and never transpose.
    public static DataValidation CloneValidation(
        DataValidation source,
        GridRange range,
        string? hostSheetName,
        int rowDelta,
        int colDelta,
        bool includeAdditionalRanges = true) =>
        CloneValidation(source, range, hostSheetName, new PasteOffsetOp(rowDelta, colDelta), includeAdditionalRanges);

    public static DataValidation CloneValidation(
        DataValidation source,
        GridRange range,
        string? hostSheetName,
        RewriteOperation? pasteOp,
        bool includeAdditionalRanges = true)
    {
        var clone = source.CloneWithNewIdentity(
            range,
            includeAdditionalRanges ? source.AdditionalRanges : []);
        clone.Formula1 = RewriteValidationFormula(source.Formula1, source.Type, hostSheetName, pasteOp);
        clone.Formula2 = RewriteValidationFormula(source.Formula2, source.Type, hostSheetName, pasteOp);

        return clone;
    }

    // pasteOp is a PasteTransposeOp (axis-swapping) for a transpose paste and a PasteOffsetOp
    // (uniform per-cell translation) otherwise -- mirrors PasteConditionalFormatsCommand's
    // pasteOp selection (CloneRuleForDestination), which applies the identical distinction to
    // ConditionalFormat.FormulaText for the same reason: transpose swaps a relative reference's
    // own (row,col) offset from the rule's own anchor onto the new anchor, which is NOT the
    // uniform (rowDelta,colDelta) translation PasteOffsetOp performs.
    private static string? RewriteValidationFormula(string? formula, DvType type, string? hostSheetName, RewriteOperation? pasteOp)
    {
        if (string.IsNullOrWhiteSpace(formula) || hostSheetName is null || pasteOp is null)
            return formula;
        if (pasteOp is PasteOffsetOp { RowDelta: 0, ColDelta: 0 })
            return formula;

        // Real DV formulas are stored per OOXML convention with NO leading '=' (see
        // XlsxDataValidationClosedXmlMapper.Load, DataValidationBoundsParser.TryEvaluateBoundFormula,
        // and DataValidationService.ValidateCustom, which all defensively prepend '=' for this reason).
        // Track whether the caller's text carried an explicit '=' so we can restore the exact storage
        // form afterwards, but don't let its absence block the rewrite.
        var trimmed = formula.TrimStart();
        var hasLeadingEquals = trimmed.StartsWith('=');
        var expression = hasLeadingEquals ? trimmed[1..] : trimmed;

        // A List rule's Source is authored into Formula1 in one of two textually similar
        // shapes: a genuine range/named-range formula (always written with a leading '=', e.g.
        // "=$A$1:$A$5") or an inline literal list of items (e.g. "Yes,No" or even a single
        // cell-ref-shaped item like "A1") that must be copied verbatim. The leading '=' is the
        // actual runtime authority on which shape it is -- see DataValidationService.ListSources
        // (ValidateList/ResolveListValues), which branches on source.StartsWith('=') the same
        // way. Without this guard, an inline literal such as "A1" satisfies
        // LooksLikeCellReferenceFormula below (it contains a digit) and gets silently rewritten
        // into a shifted cell reference (e.g. "B2") instead of being preserved as-is.
        if (type == DvType.List && !hasLeadingEquals)
            return formula;

        if (!LooksLikeCellReferenceFormula(expression))
            return formula;

        // A comma is just as likely to be a function-argument separator (e.g. AND(A1>0,B1>0)) as an
        // inline list-literal separator (e.g. "1,2,3") -- FormulaRewriter is a full AST rewriter and
        // handles the former fine, while the latter simply fails to parse as an expression and falls
        // through to the unchanged-formula return below.
        var rewritten = FormulaRewriter.Rewrite(expression, pasteOp, hostSheetName);
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
