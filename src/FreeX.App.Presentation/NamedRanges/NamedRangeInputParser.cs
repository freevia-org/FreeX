using FreeX.Core.Model;

namespace FreeX.App.Presentation.NamedRanges;

public static class NamedRangeInputParser
{
    public static bool TryParseRange(Workbook workbook, string input, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input) || workbook.SheetCount == 0)
            return false;

        return TryParseRange(workbook, workbook.GetSheetAt(0).Id, input, out range);
    }

    public static bool TryParseRange(
        Workbook workbook,
        SheetId defaultSheetId,
        string input,
        out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input) || workbook.SheetCount == 0)
            return false;

        var normalized = input.Trim();
        if (normalized.StartsWith('='))
            normalized = normalized[1..].Trim();

        // Sheet-scope-first: a named FORMULA scoped to defaultSheetId shadows a same-named
        // workbook-global named RANGE (Excel scope precedence is per-NAME, not per-kind).
        // Workbook.TryGetNamedRange only distinguishes scoped-range vs. global-range and would
        // otherwise return the shadowed global range as if it were the correct match -- mirrors
        // NamedRangeNodeScopeResolver.TryResolveNamedRange's identical rule for AST-based
        // resolution (Core.Commands can't share that internal type from this assembly). This
        // guard is deliberately explicit here (rather than relying solely on
        // Workbook.TryGetNamedRange's own internal handling of this case) so this UI-facing
        // entry point stays correct on its own terms.
        if (!workbook.ScopedNamedFormulas.ContainsKey((normalized, defaultSheetId)) &&
            workbook.TryGetNamedRange(normalized, defaultSheetId, out range))
            return true;

        return WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            normalized,
            sheetName => workbook.GetSheet(sheetName)?.Id,
            out range);
    }
}
