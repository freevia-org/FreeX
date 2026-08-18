using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class FormulaAuditingService
{
    private static IReadOnlyList<CellAddress> ExtractPrecedents(Workbook workbook, SheetId hostSheetId, string formulaText)
    {
        try
        {
            var ast = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            var result = new HashSet<CellAddress>();
            CollectReferences(workbook, hostSheetId, ast, result);
            return SortByWorkbookOrder(workbook, result).ToList();
        }
        catch (FormulaParseException)
        {
            return [];
        }
    }

    private static void CollectReferences(
        Workbook workbook,
        SheetId hostSheetId,
        FormulaNode node,
        HashSet<CellAddress> result)
    {
        switch (node)
        {
            case CellRefNode cellRef:
                if (ResolveSheet(workbook, hostSheetId, cellRef.SheetName) is { } cellSheet)
                    result.Add(new CellAddress(cellSheet.Id, cellRef.Row, cellRef.ColumnNumber));
                break;

            case RangeRefNode rangeRef:
                if (ResolveSheet(workbook, hostSheetId, rangeRef.SheetName ?? rangeRef.Start.SheetName) is { } rangeSheet)
                    AddRange(result, rangeSheet.Id, rangeRef);
                break;

            case NamedRangeNode namedRange:
                // Sheet-qualification-aware: an explicit qualifier (e.g. "Sheet2" in
                // "Sheet2!Data") must resolve against THAT sheet's own scope, not hostSheetId
                // (the formula's own sheet) -- otherwise a formula like "=SUM(Sheet2!Data)"
                // written on Sheet1, where Sheet1 ALSO has its own local "Data", would trace
                // precedents against Sheet1's "Data" instead of Sheet2's
                // (R92-io-defined-name-scope-eval-5-3). Also preserves the existing sheet-scope-
                // first precedence (a sheet-scoped named FORMULA shadows a same-named
                // workbook-global named RANGE at the resolved scope -- mirrors
                // RecalcEngine.CollectReferences's NamedRangeNode handling). See
                // NamedRangeNodeScopeResolver for the shared rule.
                if (NamedRangeNodeScopeResolver.TryResolveNamedRange(workbook, namedRange, hostSheetId, out var range))
                    foreach (var address in range.AllCells())
                        result.Add(address);
                break;

            case StructuredReferenceNode structured:
                if (StructuredReferenceResolver.ResolveDataBodyColumn(
                        workbook,
                        workbook.GetSheet(hostSheetId),
                        structured.TableName,
                        structured.ColumnName) is { } structuredRange)
                    foreach (var address in structuredRange.AllCells())
                        result.Add(address);
                break;

            case BinaryOpNode binary:
                CollectReferences(workbook, hostSheetId, binary.Left, result);
                CollectReferences(workbook, hostSheetId, binary.Right, result);
                break;

            case UnaryOpNode unary:
                CollectReferences(workbook, hostSheetId, unary.Operand, result);
                break;

            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    CollectReferences(workbook, hostSheetId, arg, result);
                break;
        }
    }

    private static IReadOnlyList<GridRange> ExtractPrecedentRegions(Workbook workbook, SheetId hostSheetId, string formulaText)
    {
        try
        {
            var ast = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            var result = new List<GridRange>();
            CollectReferenceRegions(workbook, hostSheetId, ast, result);
            return result;
        }
        catch (FormulaParseException)
        {
            return [];
        }
    }

    // Mirrors CollectReferences above but preserves a multi-cell RangeRefNode/NamedRangeNode/
    // StructuredReferenceNode as ONE contiguous GridRange region instead of flattening it into
    // individual CellAddress entries. Used only by the trace-arrow builders (this service's
    // CollectPrecedentTraceArrows and FormulaTraceArrowPlanner) so a range precedent collapses
    // into a single arrow instead of one arrow per cell in the range
    // (R88-app-formula-auditing-5-3), while GetDirectPrecedents/GetDirectDependents keep their
    // existing per-cell contract unchanged for every other caller (Ctrl+[ navigation, Go To
    // Special, dependents lookup, etc.).
    private static void CollectReferenceRegions(
        Workbook workbook,
        SheetId hostSheetId,
        FormulaNode node,
        List<GridRange> result)
    {
        switch (node)
        {
            case CellRefNode cellRef:
                if (ResolveSheet(workbook, hostSheetId, cellRef.SheetName) is { } cellSheet)
                {
                    var addr = new CellAddress(cellSheet.Id, cellRef.Row, cellRef.ColumnNumber);
                    result.Add(new GridRange(addr, addr));
                }
                break;

            case RangeRefNode rangeRef:
                if (ResolveSheet(workbook, hostSheetId, rangeRef.SheetName ?? rangeRef.Start.SheetName) is { } rangeSheet)
                {
                    result.Add(new GridRange(
                        new CellAddress(rangeSheet.Id, rangeRef.Start.Row, rangeRef.Start.ColumnNumber),
                        new CellAddress(rangeSheet.Id, rangeRef.End.Row, rangeRef.End.ColumnNumber)));
                }
                break;

            case NamedRangeNode namedRange:
                // Sheet-qualification-aware, mirrors CollectReferences's NamedRangeNode handling
                // above (R92-io-defined-name-scope-eval-5-3).
                if (NamedRangeNodeScopeResolver.TryResolveNamedRange(workbook, namedRange, hostSheetId, out var range))
                    result.Add(range);
                break;

            case StructuredReferenceNode structured:
                if (StructuredReferenceResolver.ResolveDataBodyColumn(
                        workbook,
                        workbook.GetSheet(hostSheetId),
                        structured.TableName,
                        structured.ColumnName) is { } structuredRange)
                    result.Add(structuredRange);
                break;

            case BinaryOpNode binary:
                CollectReferenceRegions(workbook, hostSheetId, binary.Left, result);
                CollectReferenceRegions(workbook, hostSheetId, binary.Right, result);
                break;

            case UnaryOpNode unary:
                CollectReferenceRegions(workbook, hostSheetId, unary.Operand, result);
                break;

            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    CollectReferenceRegions(workbook, hostSheetId, arg, result);
                break;
        }
    }

    private static bool HasAnyBlankPrecedent(Workbook workbook, SheetId hostSheetId, string formulaText)
    {
        if (TryHasAnyBlankLocalCellReference(workbook, hostSheetId, formulaText, out var hasBlankLocalReference))
            return hasBlankLocalReference;

        try
        {
            var ast = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            return ReferencesBlankPrecedent(workbook, hostSheetId, ast);
        }
        catch (FormulaParseException)
        {
            return false;
        }
    }

    private static bool TryHasAnyBlankLocalCellReference(
        Workbook workbook,
        SheetId hostSheetId,
        string formulaText,
        out bool hasBlankPrecedent)
    {
        hasBlankPrecedent = false;

        for (var index = 0; index < formulaText.Length; index++)
        {
            if (formulaText[index] is '"' or '\'' or '!' or ':' or '[' or ']')
                return false;
        }

        for (var index = 0; index < formulaText.Length; index++)
        {
            var ch = formulaText[index];
            if (ch != '$' && !IsAsciiLetter(ch) && ch != '_')
                continue;

            if (!IsFormulaReferenceBoundaryBefore(formulaText, index))
            {
                if (ch != '$' && IsFunctionCallToken(formulaText, index, out var midTokenFunctionEnd))
                {
                    index = midTokenFunctionEnd - 1;
                    continue;
                }

                return false;
            }

            // A token immediately followed by '(' (optionally through whitespace) is a function
            // name (e.g. LOG10, ATAN2), not a cell reference, even when its letters+digits also
            // happen to parse as a valid A1-style address. Check this BEFORE attempting to read it
            // as a reference, since TryReadFormulaReference has no way to see past the token.
            if (ch != '$' && IsFunctionCallToken(formulaText, index, out var functionEnd))
            {
                index = functionEnd - 1;
                continue;
            }

            if (TryReadFormulaReference(formulaText, index, out var end, out var row, out var col))
            {
                if (IsBlankPrecedent(workbook, new CellAddress(hostSheetId, row, col)))
                {
                    hasBlankPrecedent = true;
                    return true;
                }

                index = end - 1;
                continue;
            }

            if (ch == '$')
                return false;

            return false;
        }

        return true;
    }

    private static bool ReferencesBlankPrecedent(Workbook workbook, SheetId hostSheetId, FormulaNode node)
    {
        switch (node)
        {
            case CellRefNode cellRef:
                return ResolveSheet(workbook, hostSheetId, cellRef.SheetName) is { } cellSheet &&
                       IsBlankPrecedent(workbook, new CellAddress(cellSheet.Id, cellRef.Row, cellRef.ColumnNumber));

            case RangeRefNode rangeRef:
                return ResolveSheet(workbook, hostSheetId, rangeRef.SheetName ?? rangeRef.Start.SheetName) is { } rangeSheet &&
                       RangeContainsBlankPrecedent(workbook, rangeSheet.Id, rangeRef);

            case NamedRangeNode namedRange:
                // Sheet-qualification-aware: mirrors CollectReferences's NamedRangeNode handling
                // above (R92-io-defined-name-scope-eval-5-3).
                return NamedRangeNodeScopeResolver.TryResolveNamedRange(workbook, namedRange, hostSheetId, out var range) &&
                       RangeContainsBlankPrecedent(workbook, range);

            case StructuredReferenceNode structured:
                return StructuredReferenceResolver.ResolveDataBodyColumn(
                        workbook,
                        workbook.GetSheet(hostSheetId),
                        structured.TableName,
                        structured.ColumnName) is { } structuredRange &&
                       RangeContainsBlankPrecedent(workbook, structuredRange);

            case BinaryOpNode binary:
                return ReferencesBlankPrecedent(workbook, hostSheetId, binary.Left) ||
                       ReferencesBlankPrecedent(workbook, hostSheetId, binary.Right);

            case UnaryOpNode unary:
                return ReferencesBlankPrecedent(workbook, hostSheetId, unary.Operand);

            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    if (ReferencesBlankPrecedent(workbook, hostSheetId, arg))
                        return true;

                return false;

            default:
                return false;
        }
    }

    private static bool RangeContainsBlankPrecedent(Workbook workbook, SheetId sheetId, RangeRefNode range)
    {
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);

        return RangeContainsBlankPrecedent(workbook, sheetId, startRow, endRow, startCol, endCol);
    }

    private static bool RangeContainsBlankPrecedent(Workbook workbook, GridRange range) =>
        RangeContainsBlankPrecedent(
            workbook,
            range.Start.Sheet,
            Math.Min(range.Start.Row, range.End.Row),
            Math.Max(range.Start.Row, range.End.Row),
            Math.Min(range.Start.Col, range.End.Col),
            Math.Max(range.Start.Col, range.End.Col));

    private static bool RangeContainsBlankPrecedent(
        Workbook workbook,
        SheetId sheetId,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                if (IsBlankPrecedent(workbook, new CellAddress(sheetId, row, col)))
                    return true;
            }
        }

        return false;
    }

    private static Sheet? ResolveSheet(Workbook workbook, SheetId hostSheetId, string? sheetName)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
            return workbook.GetSheet(sheetName);

        return workbook.GetSheet(hostSheetId);
    }

    /// <summary>
    /// True when <paramref name="formulaAddress"/>'s formula references at least one cell/range in
    /// ANOTHER workbook (the bracketed <c>[Book.xlsx]Sheet</c> / <c>'[Book.xlsx]Sheet'</c>
    /// external-reference syntax; see <see cref="ExternalSheetReferenceResolver"/>).
    /// <see cref="ResolveSheet"/> can only resolve a sheet name against THIS workbook's own sheet
    /// list, so <see cref="CollectReferences"/>/<see cref="CollectReferenceRegions"/> silently
    /// SKIP an external reference -- there is no in-workbook <see cref="Sheet"/>/
    /// <see cref="CellAddress"/> to represent it as. That means <see cref="GetDirectPrecedents"/>
    /// and <see cref="GetDirectPrecedentRegions"/> can return an EMPTY list for a formula that
    /// genuinely has a precedent, just not one FreeX can point a <see cref="CellAddress"/> at, and
    /// a caller that reads an empty list as "no direct precedents" is reporting something false
    /// (R142-core-commands-formula-auditing-trace-precedents-external-workbook-misreport). Callers
    /// use this flag to distinguish that case and say so explicitly instead.
    ///
    /// What the UI should draw for one: Excel itself never draws a navigable arrow to an external
    /// reference either -- since the source workbook isn't open there is nothing in the local grid
    /// to point at -- it draws a small worksheet icon at the traced cell with no line to anywhere.
    /// FreeX's nearest existing analogue is <c>FormulaTraceOverlayPlanner.CrossSheetMarker</c> (see
    /// FreeX.App.Presentation/FormulaAuditing/FormulaTraceOverlayPlanner.cs), but that marker's
    /// contract is "click to navigate to a real CellAddress on another LOCAL sheet" -- it cannot
    /// represent a workbook that was never opened, so it must not be reused unmodified for this.
    /// Until the trace-arrow overlay contract grows a distinct, non-navigable external-reference
    /// marker kind, the accurate status-bar/message-box notice this flag drives (see
    /// FreeX.App.Host/MainWindow.FormulaCommands.cs TracePrecedentsForCell and
    /// FreeX.App.Avalonia/MainWindow.FormulaAuditing.cs TraceFormulaPrecedents) is what stands in
    /// for it, so the user is told the truth instead of "no direct precedents".
    /// </summary>
    public static bool HasExternalPrecedentReference(Workbook workbook, CellAddress formulaAddress)
    {
        var sheet = workbook.GetSheet(formulaAddress.Sheet);
        var cell = sheet?.GetCell(formulaAddress);
        if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        try
        {
            var ast = new Parser(new Lexer(cell.FormulaText).Tokenize()).Parse();
            return ReferencesExternalWorkbook(ast);
        }
        catch (FormulaParseException)
        {
            return false;
        }
    }

    private static bool ReferencesExternalWorkbook(FormulaNode node)
    {
        switch (node)
        {
            case CellRefNode cellRef:
                return cellRef.SheetName is { } cellSheetName &&
                       ExternalSheetReferenceResolver.IsExternalReferenceSyntax(cellSheetName);

            case RangeRefNode rangeRef:
                return (rangeRef.SheetName ?? rangeRef.Start.SheetName) is { } rangeSheetName &&
                       ExternalSheetReferenceResolver.IsExternalReferenceSyntax(rangeSheetName);

            case BinaryOpNode binary:
                return ReferencesExternalWorkbook(binary.Left) || ReferencesExternalWorkbook(binary.Right);

            case UnaryOpNode unary:
                return ReferencesExternalWorkbook(unary.Operand);

            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    if (ReferencesExternalWorkbook(arg))
                        return true;

                return false;

            default:
                return false;
        }
    }

    private static void AddRange(HashSet<CellAddress> result, SheetId sheetId, RangeRefNode range)
    {
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);

        for (var row = startRow; row <= endRow; row++)
            for (var col = startCol; col <= endCol; col++)
                result.Add(new CellAddress(sheetId, row, col));
    }

    private static IEnumerable<CellAddress> SortByWorkbookOrder(Workbook workbook, IEnumerable<CellAddress> addresses)
    {
        var sheetOrder = workbook.Sheets
            .Select((sheet, index) => (sheet.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        return addresses
            .OrderBy(address => sheetOrder.GetValueOrDefault(address.Sheet, int.MaxValue))
            .ThenBy(address => address.Row)
            .ThenBy(address => address.Col);
    }
}
