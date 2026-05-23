using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using System.Globalization;
using System.Text.RegularExpressions;

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
        new(FormulaAuditingService.InconsistentFormulaErrorCode, "Formulas inconsistent with nearby formulas", "Flag formulas whose relative reference pattern differs from adjacent formulas."),
        new(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode, "Formulas which omit cells in a region", "Flag SUM formulas that omit adjacent cells in the region."),
        new(FormulaAuditingService.UnlockedFormulaCellsErrorCode, "Unlocked cells containing formulas", "Flag formula cells that are not locked for worksheet protection."),
        new(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode, "Formulas referring to blank cells", "Flag formulas that refer to blank cells."),
        new(FormulaAuditingService.TwoDigitYearTextDateErrorCode, "Cells containing years represented as 2 digits", "Flag text dates whose year is entered with only two digits."),
        new(FormulaAuditingService.NumberStoredAsTextErrorCode, "Numbers formatted as text or preceded by an apostrophe", "Flag numbers stored as text.")
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
            return new CommandOutcome(false, "No cell exists at the selected issue.");

        if (_ignored && !FormulaAuditingService.HasIgnorableFormulaIssue(ctx.Workbook, _sheetId, _address, cell))
            return new CommandOutcome(false, "The selected cell does not currently contain an issue.");

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

public static partial class FormulaAuditingService
{
    public const string FormulaRefersToBlankCellsErrorCode = "FormulaRefersToBlankCells";
    public const string NumberStoredAsTextErrorCode = "NumberStoredAsText";
    public const string TwoDigitYearTextDateErrorCode = "TwoDigitYearTextDate";
    public const string InconsistentFormulaErrorCode = "InconsistentFormula";
    public const string FormulaOmitsAdjacentCellsErrorCode = "FormulaOmitsAdjacentCells";
    public const string UnlockedFormulaCellsErrorCode = "UnlockedFormulaCells";

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
        var sheetOrder = workbook.Sheets
            .Select((sheet, index) => (sheet.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        var result = FindFormulaErrors(workbook, sheetId)
            .Select(error => new FormulaErrorIssue(
                error.SheetId,
                error.SheetName,
                error.Address,
                error.Address.ToA1(),
                error.Error.Code,
                error.FormulaText is null ? null : "=" + error.FormulaText,
                DescribeError(error.Error)))
            .ToList();

        if (!workbook.DisabledFormulaErrorCodes.Contains(NumberStoredAsTextErrorCode))
            result.AddRange(FindNumbersStoredAsTextIssues(workbook, sheetId));

        if (!workbook.DisabledFormulaErrorCodes.Contains(TwoDigitYearTextDateErrorCode))
            result.AddRange(FindTwoDigitYearTextDateIssues(workbook, sheetId));

        if (!workbook.DisabledFormulaErrorCodes.Contains(FormulaRefersToBlankCellsErrorCode))
            result.AddRange(FindFormulaRefersToBlankCellsIssues(workbook, sheetId));

        if (!workbook.DisabledFormulaErrorCodes.Contains(InconsistentFormulaErrorCode))
            result.AddRange(FindInconsistentFormulaIssues(workbook, sheetId));

        if (!workbook.DisabledFormulaErrorCodes.Contains(FormulaOmitsAdjacentCellsErrorCode))
            result.AddRange(FindFormulaOmitsAdjacentCellsIssues(workbook, sheetId));

        if (!workbook.DisabledFormulaErrorCodes.Contains(UnlockedFormulaCellsErrorCode))
            result.AddRange(FindUnlockedFormulaCellIssues(workbook, sheetId));

        return result
            .OrderBy(issue => sheetOrder.GetValueOrDefault(issue.SheetId, int.MaxValue))
            .ThenBy(issue => issue.Address.Row)
            .ThenBy(issue => issue.Address.Col)
            .ToList();
    }

    internal static bool HasIgnorableFormulaIssue(Workbook workbook, SheetId sheetId, CellAddress address, Cell cell) =>
        cell.Value is ErrorValue ||
        (!cell.HasFormula && cell.Value is TextValue text && IsNumberStoredAsText(text.Value)) ||
        (!cell.HasFormula && cell.Value is TextValue dateText && IsTextDateWithTwoDigitYear(dateText.Value)) ||
        FormulaRefersToBlankCells(workbook, sheetId, cell) ||
        IsInconsistentFormula(workbook, sheetId, address) ||
        FormulaOmitsAdjacentCells(workbook, sheetId, address, cell) ||
        IsUnlockedFormulaCell(workbook, cell);

    private static bool HasIgnorableLiteralIssue(Cell cell) =>
        cell.Value is ErrorValue ||
        (!cell.HasFormula && cell.Value is TextValue text && IsNumberStoredAsText(text.Value));

    private static IEnumerable<FormulaErrorIssue> FindNumbersStoredAsTextIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError || !HasIgnorableLiteralIssue(cell) || cell.Value is not TextValue)
                    continue;

                yield return new FormulaErrorIssue(
                    sheet.Id,
                    sheet.Name,
                    address,
                    address.ToA1(),
                    NumberStoredAsTextErrorCode,
                    null,
                    "The number in this cell is formatted as text or preceded by an apostrophe.");
            }
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindFormulaRefersToBlankCellsIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError || !FormulaRefersToBlankCells(workbook, sheet.Id, cell))
                    continue;

                yield return new FormulaErrorIssue(
                    sheet.Id,
                    sheet.Name,
                    address,
                    address.ToA1(),
                    FormulaRefersToBlankCellsErrorCode,
                    cell.FormulaText is null ? null : "=" + cell.FormulaText,
                    "The formula refers to one or more blank cells.");
            }
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindTwoDigitYearTextDateIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError || cell.HasFormula || cell.Value is not TextValue text || !IsTextDateWithTwoDigitYear(text.Value))
                    continue;

                yield return new FormulaErrorIssue(
                    sheet.Id,
                    sheet.Name,
                    address,
                    address.ToA1(),
                    TwoDigitYearTextDateErrorCode,
                    null,
                    "The text date in this cell contains a two-digit year.");
            }
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindInconsistentFormulaIssues(Workbook workbook, SheetId? sheetId)
    {
        var flagged = new HashSet<CellAddress>();
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            var formulas = sheet.EnumerateCells()
                .Where(item => item.Cell.HasFormula && !item.Cell.IgnoreFormulaError && !string.IsNullOrWhiteSpace(item.Cell.FormulaText))
                .Select(item => new FormulaPattern(item.Address, item.Cell.FormulaText!, NormalizeFormulaPattern(item.Address, item.Cell.FormulaText!)))
                .ToList();

            foreach (var issue in FindInconsistentFormulaRuns(sheet, formulas.GroupBy(item => item.Address.Row), flagged))
                yield return issue;

            foreach (var issue in FindInconsistentFormulaRuns(sheet, formulas.GroupBy(item => item.Address.Col), flagged))
                yield return issue;
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindInconsistentFormulaRuns(
        Sheet sheet,
        IEnumerable<IGrouping<uint, FormulaPattern>> groupedFormulas,
        HashSet<CellAddress> flagged)
    {
        foreach (var group in groupedFormulas)
        {
            var formulas = group.OrderBy(item => item.Address.Row).ThenBy(item => item.Address.Col).ToList();
            foreach (var run in SplitAdjacentFormulaRuns(formulas))
            {
                if (run.Count < 3)
                    continue;

                var patternGroups = run
                    .GroupBy(item => item.Pattern, StringComparer.Ordinal)
                    .OrderByDescending(item => item.Count())
                    .ToList();

                if (patternGroups.Count < 2 || patternGroups[0].Count() < 2)
                    continue;

                foreach (var outlier in patternGroups.Where(item => item.Count() == 1).SelectMany(item => item))
                {
                    if (!flagged.Add(outlier.Address))
                        continue;

                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        outlier.Address,
                        outlier.Address.ToA1(),
                        InconsistentFormulaErrorCode,
                        "=" + outlier.FormulaText,
                        "The formula is inconsistent with nearby formulas.");
                }
            }
        }
    }

    private static IEnumerable<List<FormulaPattern>> SplitAdjacentFormulaRuns(IReadOnlyList<FormulaPattern> formulas)
    {
        var run = new List<FormulaPattern>();
        FormulaPattern? previous = null;
        foreach (var formula in formulas)
        {
            if (previous is not null &&
                Math.Abs((int)formula.Address.Row - (int)previous.Address.Row) +
                Math.Abs((int)formula.Address.Col - (int)previous.Address.Col) != 1)
            {
                yield return run;
                run = [];
            }

            run.Add(formula);
            previous = formula;
        }

        if (run.Count > 0)
            yield return run;
    }

    private static bool IsInconsistentFormula(Workbook workbook, SheetId sheetId, CellAddress address) =>
        FindInconsistentFormulaIssues(workbook, sheetId)
            .Any(issue => issue.Address == address);

    private sealed record FormulaPattern(CellAddress Address, string FormulaText, string Pattern);

    private static IEnumerable<FormulaErrorIssue> FindFormulaOmitsAdjacentCellsIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError || !FormulaOmitsAdjacentCells(workbook, sheet.Id, address, cell))
                    continue;

                yield return new FormulaErrorIssue(
                    sheet.Id,
                    sheet.Name,
                    address,
                    address.ToA1(),
                    FormulaOmitsAdjacentCellsErrorCode,
                    cell.FormulaText is null ? null : "=" + cell.FormulaText,
                    "The formula omits adjacent cells in the region.");
            }
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindUnlockedFormulaCellIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError || !IsUnlockedFormulaCell(workbook, cell))
                    continue;

                yield return new FormulaErrorIssue(
                    sheet.Id,
                    sheet.Name,
                    address,
                    address.ToA1(),
                    UnlockedFormulaCellsErrorCode,
                    cell.FormulaText is null ? null : "=" + cell.FormulaText,
                    "The formula cell is unlocked and may be changed when the worksheet is protected.");
            }
        }
    }

    private static bool IsUnlockedFormulaCell(Workbook workbook, Cell cell) =>
        cell.HasFormula && !workbook.GetStyle(cell.StyleId).Locked;

    private static bool FormulaOmitsAdjacentCells(Workbook workbook, SheetId sheetId, CellAddress formulaAddress, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        foreach (var range in ExtractSumRanges(sheetId, cell.FormulaText))
        {
            if (range.Start.Sheet != range.End.Sheet)
                continue;

            if (IsVerticalRange(range) && HasIncludedValues(workbook, range))
            {
                if (range.Start.Row > 1 &&
                    HasValueAt(workbook, new CellAddress(range.Start.Sheet, range.Start.Row - 1, range.Start.Col)))
                    return true;

                if (HasValueAt(workbook, new CellAddress(range.End.Sheet, range.End.Row + 1, range.End.Col)))
                    return true;
            }

            if (IsHorizontalRange(range) && HasIncludedValues(workbook, range))
            {
                if (range.Start.Col > 1 && HasValueAt(workbook, new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - 1)))
                    return true;

                if (HasValueAt(workbook, new CellAddress(range.End.Sheet, range.End.Row, range.End.Col + 1)))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<GridRange> ExtractSumRanges(SheetId sheetId, string formulaText)
    {
        foreach (Match match in Regex.Matches(
                     formulaText,
                     @"\bSUM\s*\(\s*(\$?[A-Za-z]{1,3}\$?[0-9]{1,7})\s*:\s*(\$?[A-Za-z]{1,3}\$?[0-9]{1,7})\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return new GridRange(
                ParseLocalAddress(sheetId, match.Groups[1].Value),
                ParseLocalAddress(sheetId, match.Groups[2].Value));
        }
    }

    private static bool IsVerticalRange(GridRange range) =>
        range.Start.Col == range.End.Col && range.Start.Row < range.End.Row;

    private static bool IsHorizontalRange(GridRange range) =>
        range.Start.Row == range.End.Row && range.Start.Col < range.End.Col;

    private static bool HasIncludedValues(Workbook workbook, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (HasValueAt(workbook, new CellAddress(range.Start.Sheet, row, col)))
                    return true;
            }
        }

        return false;
    }

    private static bool HasValueAt(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        return cell is not null && !cell.HasFormula && cell.Value is not BlankValue;
    }

    private static CellAddress ParseLocalAddress(SheetId sheetId, string token)
    {
        var normalized = token.Replace("$", string.Empty, StringComparison.Ordinal);
        var match = Regex.Match(normalized, @"^([A-Za-z]{1,3})([0-9]{1,7})$", RegexOptions.CultureInvariant);
        var col = CellAddress.ColumnNameToNumber(match.Groups[1].Value);
        var row = uint.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return new CellAddress(sheetId, row, col);
    }

    private static string NormalizeFormulaPattern(CellAddress address, string formulaText) =>
        Regex.Replace(
            formulaText,
            @"(?<![A-Za-z0-9_])\$?([A-Za-z]{1,3})\$?([0-9]{1,7})(?![A-Za-z0-9_])",
            match =>
            {
                var col = CellAddress.ColumnNameToNumber(match.Groups[1].Value);
                var row = uint.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return $"R[{(int)row - (int)address.Row}]C[{(int)col - (int)address.Col}]";
            },
            RegexOptions.CultureInvariant);

    private static bool FormulaRefersToBlankCells(Workbook workbook, SheetId sheetId, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        return ExtractPrecedents(workbook, sheetId, cell.FormulaText)
            .Any(precedent => IsBlankPrecedent(workbook, precedent));
    }

    private static bool IsBlankPrecedent(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        return cell is null || (!cell.HasFormula && cell.Value is BlankValue);
    }

    private static bool IsNumberStoredAsText(string text) =>
        double.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var value)
        && !double.IsNaN(value)
        && !double.IsInfinity(value);

    private static bool IsTextDateWithTwoDigitYear(string text)
    {
        var value = text.Trim();
        if (value.Length < 6)
            return false;

        if (Regex.IsMatch(value, @"^\d{1,2}[/-]\d{1,2}[/-]\d{2}$", RegexOptions.CultureInvariant))
            return DateTime.TryParseExact(
                value,
                ["M/d/yy", "MM/dd/yy", "M-d-yy", "MM-dd-yy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);

        if (Regex.IsMatch(value, @"^[A-Za-z]{3,9}\s+\d{1,2},\s*\d{2}$", RegexOptions.CultureInvariant))
            return DateTime.TryParseExact(
                value,
                ["MMM d, yy", "MMM dd, yy", "MMMM d, yy", "MMMM dd, yy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);

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

}
