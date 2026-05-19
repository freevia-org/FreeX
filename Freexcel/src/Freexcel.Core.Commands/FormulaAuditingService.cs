using System.Globalization;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;

namespace Freexcel.Core.Commands;

public sealed record FormulaErrorInfo(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    ErrorValue Error,
    string? FormulaText);

public sealed record FormulaErrorIssue(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string Cell,
    string ErrorCode,
    string? FormulaText,
    string Description);

public sealed record FormulaErrorCheckingRule(string ErrorCode, string Label, string Description);

public static class FormulaErrorCheckingRuleCatalog
{
    public const string NumberStoredAsTextCode = "NumberStoredAsText";
    public const string FormulaRefersToBlankCellsCode = "FormulaRefersToBlankCells";

    public static IReadOnlyList<FormulaErrorCheckingRule> SupportedRules { get; } =
    [
        new(ErrorValue.DivByZero.Code, "Formulas that divide by zero", "Flag formulas that result in #DIV/0!."),
        new(ErrorValue.Value.Code, "Formulas with incompatible values", "Flag formulas that result in #VALUE!."),
        new(ErrorValue.Ref.Code, "Formulas with invalid cell references", "Flag formulas that result in #REF!."),
        new(ErrorValue.Name.Code, "Formulas with unrecognized names", "Flag formulas that result in #NAME?."),
        new(ErrorValue.NA.Code, "Formulas returning #N/A", "Flag formulas that result in #N/A."),
        new(ErrorValue.Num.Code, "Formulas with invalid numbers", "Flag formulas that result in #NUM!."),
        new(ErrorValue.Null.Code, "Formulas with invalid intersections", "Flag formulas that result in #NULL!."),
        new(ErrorValue.Spill.Code, "Formulas with blocked spill ranges", "Flag formulas that result in #SPILL!."),
        new(ErrorValue.Circular.Code, "Formulas with circular references", "Flag formulas that result in #CIRCULAR!."),
        new(NumberStoredAsTextCode, "Numbers stored as text", "Flag text cells that parse as finite invariant-culture numbers."),
        new(FormulaRefersToBlankCellsCode, "Formulas referring to blank cells", "Flag formulas whose direct precedents include blank cells.")
    ];

    public static bool IsSupported(string errorCode) =>
        SupportedRules.Any(rule => string.Equals(rule.ErrorCode, errorCode, StringComparison.OrdinalIgnoreCase));
}

public sealed class SetFormulaErrorIgnoredCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly bool _ignored;
    private bool _previousIgnored;

    public SetFormulaErrorIgnoredCommand(SheetId sheetId, CellAddress address, bool ignored)
    {
        _sheetId = sheetId;
        _address = address;
        _ignored = ignored;
    }

    public string Label => _ignored ? "Ignore Error" : "Reset Ignored Error";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var cell = ctx.GetSheet(_sheetId).GetCell(_address);
        if (cell is null)
            return new CommandOutcome(false, "No cell exists at the selected error.");

        _previousIgnored = cell.IgnoreFormulaError;
        cell.IgnoreFormulaError = _ignored;
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var cell = ctx.GetSheet(_sheetId).GetCell(_address);
        if (cell is not null)
            cell.IgnoreFormulaError = _previousIgnored;
    }
}

public sealed class SetFormulaErrorCheckingRuleCommand : IWorkbookCommand
{
    private readonly string _errorCode;
    private readonly bool _enabled;
    private bool _wasDisabled;

    public SetFormulaErrorCheckingRuleCommand(string errorCode, bool enabled)
    {
        _errorCode = errorCode;
        _enabled = enabled;
    }

    public string Label => "Error Checking Options";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!FormulaErrorCheckingRuleCatalog.IsSupported(_errorCode))
            return new CommandOutcome(false, "Formula error checking rule is not supported.");

        _wasDisabled = ctx.Workbook.DisabledFormulaErrorCodes.Contains(_errorCode);
        if (_enabled)
            ctx.Workbook.DisabledFormulaErrorCodes.Remove(_errorCode);
        else
            ctx.Workbook.DisabledFormulaErrorCodes.Add(_errorCode);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_wasDisabled)
            ctx.Workbook.DisabledFormulaErrorCodes.Add(_errorCode);
        else
            ctx.Workbook.DisabledFormulaErrorCodes.Remove(_errorCode);
    }
}

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

    public static IReadOnlyList<FormulaTraceArrow> GetPrecedentTraceArrows(Workbook workbook, CellAddress formulaAddress)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        CollectPrecedentTraceArrows(workbook, formulaAddress, result, visited);
        return result;
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

    public static IReadOnlyList<FormulaTraceArrow> GetDependentTraceArrows(Workbook workbook, CellAddress address)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        CollectDependentTraceArrows(workbook, address, result, visited);
        return result;
    }

    private static void CollectPrecedentTraceArrows(
        Workbook workbook,
        CellAddress formulaAddress,
        List<FormulaTraceArrow> result,
        HashSet<CellAddress> visited)
    {
        if (!visited.Add(formulaAddress))
            return;

        foreach (var precedent in GetDirectPrecedents(workbook, formulaAddress))
        {
            result.Add(new FormulaTraceArrow(precedent, formulaAddress));
            CollectPrecedentTraceArrows(workbook, precedent, result, visited);
        }
    }

    private static void CollectDependentTraceArrows(
        Workbook workbook,
        CellAddress address,
        List<FormulaTraceArrow> result,
        HashSet<CellAddress> visited)
    {
        if (!visited.Add(address))
            return;

        foreach (var dependent in GetDirectDependents(workbook, address))
        {
            result.Add(new FormulaTraceArrow(address, dependent));
            CollectDependentTraceArrows(workbook, dependent, result, visited);
        }
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
                if (cell.IgnoreFormulaError)
                    continue;

                if (cell.Value is not ErrorValue error)
                    continue;

                if (workbook.DisabledFormulaErrorCodes.Contains(error.Code))
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

    public static IReadOnlyList<FormulaErrorIssue> FindFormulaErrorIssues(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<FormulaErrorIssue>();

        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells().OrderBy(c => c.Address.Row).ThenBy(c => c.Address.Col))
            {
                if (cell.IgnoreFormulaError)
                    continue;

                if (cell.Value is ErrorValue error && !workbook.DisabledFormulaErrorCodes.Contains(error.Code))
                {
                    result.Add(new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        error.Code,
                        cell.HasFormula ? "=" + cell.FormulaText : null,
                        DescribeError(error)));
                }

                if (IsNumberStoredAsTextIssue(cell) &&
                    !workbook.DisabledFormulaErrorCodes.Contains(FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode))
                {
                    result.Add(new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode,
                        null,
                        "The cell contains a number stored as text."));
                }

                if (IsFormulaRefersToBlankCellsIssue(workbook, sheet.Id, cell) &&
                    !workbook.DisabledFormulaErrorCodes.Contains(FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode))
                {
                    result.Add(new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode,
                        "=" + cell.FormulaText,
                        "The formula directly refers to one or more blank cells."));
                }
            }
        }

        return result;
    }

    private static bool IsNumberStoredAsTextIssue(Cell cell)
    {
        if (cell.HasFormula || cell.Value is not TextValue text || string.IsNullOrWhiteSpace(text.Value))
            return false;

        return double.TryParse(
                text.Value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            double.IsFinite(parsed);
    }

    private static bool IsFormulaRefersToBlankCellsIssue(Workbook workbook, SheetId sheetId, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        foreach (var precedent in ExtractPrecedents(workbook, sheetId, cell.FormulaText))
        {
            var precedentSheet = workbook.GetSheet(precedent.Sheet);
            var precedentCell = precedentSheet?.GetCell(precedent);
            if (precedentCell is null || precedentCell.Value is BlankValue)
                return true;
        }

        return false;
    }

    private static string DescribeError(ErrorValue error) => error.Code switch
    {
        "#DIV/0!" => "The formula or value results in division by zero.",
        "#VALUE!" => "The formula uses an incompatible value or argument type.",
        "#REF!" => "The formula contains an invalid cell reference.",
        "#NAME?" => "The formula contains an unrecognized name or function.",
        "#N/A" => "A value is not available to the formula.",
        "#NUM!" => "The formula contains an invalid number or numeric result.",
        "#NULL!" => "The formula specifies an invalid intersection.",
        "#SPILL!" => "The formula result cannot spill into the requested cells.",
        "#CIRCULAR!" => "The formula contains a circular reference.",
        _ => "The formula or cell contains an error value."
    };

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
