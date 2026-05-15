using Freexcel.Core.Model;
using Freexcel.Core.Formula;

namespace Freexcel.Core.Commands;

public sealed record FormulaErrorInfo(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    ErrorValue Error,
    string? FormulaText);

public static class FormulaAuditingService
{
    public static IReadOnlyList<CellAddress> GetDirectPrecedents(Workbook workbook, CellAddress formulaAddress)
    {
        var sheet = workbook.GetSheet(formulaAddress.Sheet);
        var cell = sheet?.GetCell(formulaAddress);
        if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return [];

        return ExtractPrecedents(workbook, formulaAddress.Sheet, cell.FormulaText);
    }

    public static IReadOnlyList<CellAddress> GetDirectDependents(Workbook workbook, CellAddress address)
    {
        var result = new HashSet<CellAddress>();

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (formulaAddress, cell) in sheet.EnumerateCells())
            {
                if (cell.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
                    continue;

                var precedents = ExtractPrecedents(workbook, sheet.Id, cell.FormulaText);
                if (precedents.Contains(address))
                    result.Add(formulaAddress);
            }
        }

        return SortByWorkbookOrder(workbook, result).ToList();
    }

    public static IReadOnlyList<FormulaErrorInfo> FindFormulaErrors(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<FormulaErrorInfo>();

        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells().OrderBy(c => c.Address.Row).ThenBy(c => c.Address.Col))
            {
                if (cell.Value is not ErrorValue error)
                    continue;

                result.Add(new FormulaErrorInfo(
                    sheet.Id,
                    sheet.Name,
                    address,
                    error,
                    cell.HasFormula ? cell.FormulaText : null));
            }
        }

        return result;
    }

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
