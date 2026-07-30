using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Shared sheet-qualification-aware resolver for a <see cref="NamedRangeNode"/> AST node, used
/// by every Core.Commands consumer that resolves a defined-name RANGE reference outside the full
/// formula evaluator (Data Validation list sources, formula auditing / trace precedents). Mirrors
/// <c>FormulaEvaluator.References.cs</c>'s <c>TryResolveSheetQualifiedName</c> scope-resolution
/// rule: an explicit <see cref="NamedRangeNode.SheetQualifier"/> (the "Sheet2" in "Sheet2!Data")
/// always resolves against THAT sheet's own defined-name scope, independent of the formula's own
/// host sheet, falling back to the host sheet's scope only when the reference is unqualified.
/// Also mirrors the evaluator's per-NAME (not per-kind) scope precedence: a sheet-scoped named
/// FORMULA of the same text shadows a same-named workbook-global named RANGE at that scope, so
/// <see cref="Workbook.TryGetNamedRange(string, SheetId, out GridRange)"/> (which only knows about
/// scoped vs. global RANGES) must not be consulted when a scoped FORMULA of that name exists at
/// the resolved scope.
///
/// Centralizing this here means a future consumer calls one resolver instead of re-deriving the
/// same rule (and risking the same omission the evaluator itself once had) -- see
/// R92-io-defined-name-scope-eval-5-1 (RecalcEngine.CollectReferences, already fixed inline in
/// Core.Calc, a different assembly so it can't share this exact type but follows the identical
/// rule), 5-2 (<see cref="DataValidationService"/>), and 5-3 (<see cref="FormulaAuditingService"/>).
/// </summary>
internal static class NamedRangeNodeScopeResolver
{
    /// <summary>
    /// Resolves a <see cref="NamedRangeNode"/> to the <see cref="GridRange"/> it names, honoring
    /// <see cref="NamedRangeNode.SheetQualifier"/> and the scoped-formula-shadows-global-range
    /// precedence rule described on the type. Returns <see langword="false"/> when the node has no
    /// resolvable RANGE at the applicable scope: an explicit qualifier that doesn't name a real
    /// local sheet (deleted sheet, or a bracket-prefixed external-workbook qualifier), a
    /// sheet-scoped named FORMULA of the same name shadowing any global range, or a genuinely
    /// undefined name. Callers should treat <see langword="false"/> the same way the pre-existing
    /// per-call-site logic this replaces already did -- typically falling back to full formula
    /// evaluation, or treating the reference as unmatched.
    /// </summary>
    internal static bool TryResolveNamedRange(
        Workbook workbook,
        NamedRangeNode named,
        SheetId hostSheetId,
        out GridRange range)
    {
        range = default;

        var scopeSheetId = hostSheetId;
        if (named.SheetQualifier is { } sheetQualifier)
        {
            var qualifiedSheet = workbook.GetSheet(sheetQualifier);
            if (qualifiedSheet is null)
            {
                // Unresolvable qualifier (deleted sheet, or a bracket-prefixed external-workbook
                // qualifier like "[1]Sheet1"): the evaluator surfaces #REF!/reads an external
                // link cache for these, neither of which this local-workbook range resolver can
                // do -- report no local match rather than silently falling back to hostSheetId's
                // own scope (which would resolve the WRONG sheet's same-named local definition).
                return false;
            }

            scopeSheetId = qualifiedSheet.Id;
        }

        // Sheet-scope-first: a named FORMULA scoped to the resolved sheet shadows a same-named
        // workbook-global named RANGE (Excel scope precedence is per-NAME, not per-kind).
        // Workbook.TryGetNamedRange only distinguishes scoped-range vs. global-range and would
        // otherwise return the shadowed global range as if it were the correct match.
        if (workbook.ScopedNamedFormulas.ContainsKey((named.Name, scopeSheetId)))
            return false;

        return workbook.TryGetNamedRange(named.Name, scopeSheetId, out range);
    }
}
