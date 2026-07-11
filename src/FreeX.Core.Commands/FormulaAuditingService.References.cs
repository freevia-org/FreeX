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
                // Sheet-scope-first: a sheet-scoped named FORMULA of the same name shadows a
                // workbook-global named RANGE (mirrors RecalcEngine.CollectReferences's
                // NamedRangeNode handling), so a bare scope-unaware range lookup must not run
                // when the host sheet has its own scoped formula of this name.
                if (!workbook.ScopedNamedFormulas.ContainsKey((namedRange.Name, hostSheetId)) &&
                    workbook.TryGetNamedRange(namedRange.Name, hostSheetId, out var range))
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
                return workbook.TryGetNamedRange(namedRange.Name, out var range) &&
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
