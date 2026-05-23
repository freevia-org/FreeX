using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
                if (workbook.TryGetNamedRange(namedRange.Name, out var range))
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
