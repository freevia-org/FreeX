using FreeX.Core.Formula;
using FreeX.Core.Model;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeX.Core.Commands;

public static partial class FormulaAuditingService
{
    private const string OmittedAdjacentCellsAggregateFunctionPattern = "SUM|AVERAGE|COUNTA|COUNT|MIN|MAX|PRODUCT";

    private static readonly string[] OmittedAdjacentCellsAggregateFunctions =
    [
        "SUM",
        "AVERAGE",
        "COUNT",
        "COUNTA",
        "MIN",
        "MAX",
        "PRODUCT"
    ];

    public static IReadOnlyList<FormulaErrorInfo> FindFormulaErrors(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<FormulaErrorInfo>();

        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            List<FormulaErrorInfo>? sheetErrors = null;
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                if (cell.IgnoreFormulaError)
                    continue;

                if (cell.Value is not ErrorValue error)
                    continue;

                if (workbook.DisabledFormulaErrorCodes.Contains(error.Code))
                    continue;

                var address = new CellAddress(sheet.Id, row, col);
                (sheetErrors ??= []).Add(new FormulaErrorInfo(
                    sheet.Id,
                    sheet.Name,
                    address,
                    error,
                    cell.HasFormula ? cell.FormulaText : null));
            }

            if (sheetErrors is null)
                continue;

            sheetErrors.Sort(CompareFormulaErrors);
            result.AddRange(sheetErrors);
        }

        return result;
    }

    private static int CompareFormulaErrors(FormulaErrorInfo left, FormulaErrorInfo right)
    {
        var rowComparison = left.Address.Row.CompareTo(right.Address.Row);
        return rowComparison != 0
            ? rowComparison
            : left.Address.Col.CompareTo(right.Address.Col);
    }

    public static IReadOnlyList<FormulaErrorIssue> FindFormulaErrorIssues(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<FormulaErrorIssue>();
        result.AddRange(FindLiteralFormulaErrorIssues(workbook, sheetId));
        result.AddRange(FindFormulaCellIssues(workbook, sheetId));

        if (result.Count <= 1)
            return result;

        var sheetOrder = workbook.Sheets
            .Select((sheet, index) => (sheet.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        result.Sort((left, right) => CompareFormulaIssues(left, right, sheetOrder));
        return result;
    }

    private static IEnumerable<FormulaErrorIssue> FindLiteralFormulaErrorIssues(Workbook workbook, SheetId? sheetId)
    {
        var checkNumberStoredAsText = !workbook.DisabledFormulaErrorCodes.Contains(NumberStoredAsTextErrorCode);
        var checkTwoDigitYearTextDate = !workbook.DisabledFormulaErrorCodes.Contains(TwoDigitYearTextDateErrorCode);
        var checkFormulaStoredAsText = !workbook.DisabledFormulaErrorCodes.Contains(FormulaStoredAsTextErrorCode);

        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.IgnoreFormulaError)
                    continue;

                if (cell.Value is ErrorValue error &&
                    !workbook.DisabledFormulaErrorCodes.Contains(error.Code))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        error.Code,
                        cell.FormulaText is null ? null : "=" + cell.FormulaText,
                        DescribeError(error));
                }

                if (cell.HasFormula || cell.Value is not TextValue text)
                    continue;

                if (checkNumberStoredAsText && IsNumberStoredAsText(text.Value))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        NumberStoredAsTextErrorCode,
                        null,
                        "The number in this cell is formatted as text or preceded by an apostrophe.");
                }

                if (checkTwoDigitYearTextDate && IsTextDateWithTwoDigitYear(text.Value))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        TwoDigitYearTextDateErrorCode,
                        null,
                        "The text date in this cell contains a two-digit year.");
                }

                if (checkFormulaStoredAsText && IsFormulaTextLiteral(text.Value))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        FormulaStoredAsTextErrorCode,
                        null,
                        "The text in this cell starts with '=' and is stored as text instead of a formula.");
                }
            }
        }
    }

    private static int CompareFormulaIssues(
        FormulaErrorIssue left,
        FormulaErrorIssue right,
        IReadOnlyDictionary<SheetId, int> sheetOrder)
    {
        var sheetComparison = sheetOrder
            .GetValueOrDefault(left.SheetId, int.MaxValue)
            .CompareTo(sheetOrder.GetValueOrDefault(right.SheetId, int.MaxValue));
        if (sheetComparison != 0)
            return sheetComparison;

        var rowComparison = left.Address.Row.CompareTo(right.Address.Row);
        return rowComparison != 0
            ? rowComparison
            : left.Address.Col.CompareTo(right.Address.Col);
    }

    internal static bool HasIgnorableFormulaIssue(Workbook workbook, SheetId sheetId, CellAddress address, Cell cell) =>
        cell.Value is ErrorValue ||
        (!cell.HasFormula && cell.Value is TextValue text && IsNumberStoredAsText(text.Value)) ||
        (!cell.HasFormula && cell.Value is TextValue dateText && IsTextDateWithTwoDigitYear(dateText.Value)) ||
        IsFormulaStoredAsText(cell) ||
        FormulaRefersToBlankCells(workbook, sheetId, cell) ||
        IsInconsistentCalculatedColumnFormula(workbook, sheetId, address, cell) ||
        IsInconsistentFormula(workbook, sheetId, address) ||
        FormulaOmitsAdjacentCells(workbook, sheetId, cell) ||
        IsUnlockedFormulaCell(workbook, cell);

    private static IEnumerable<FormulaErrorIssue> FindFormulaRefersToBlankCellsIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in EnumerateFormulaIssueCandidates(sheet))
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

    private static IEnumerable<FormulaErrorIssue> FindFormulaCellIssues(Workbook workbook, SheetId? sheetId)
    {
        var checkBlankReferences = !workbook.DisabledFormulaErrorCodes.Contains(FormulaRefersToBlankCellsErrorCode);
        var checkInconsistentCalculatedColumnFormulas = !workbook.DisabledFormulaErrorCodes.Contains(InconsistentCalculatedColumnFormulaErrorCode);
        var checkInconsistentFormulas = !workbook.DisabledFormulaErrorCodes.Contains(InconsistentFormulaErrorCode);
        var checkOmittedAdjacentCells = !workbook.DisabledFormulaErrorCodes.Contains(FormulaOmitsAdjacentCellsErrorCode);
        var checkUnlockedFormulaCells = !workbook.DisabledFormulaErrorCodes.Contains(UnlockedFormulaCellsErrorCode);
        if (!checkBlankReferences &&
            !checkInconsistentCalculatedColumnFormulas &&
            !checkInconsistentFormulas &&
            !checkOmittedAdjacentCells &&
            !checkUnlockedFormulaCells)
        {
            yield break;
        }

        var flaggedInconsistentFormulas = checkInconsistentFormulas ? new HashSet<CellAddress>() : null;
        var flaggedCalculatedColumnFormulas = checkInconsistentCalculatedColumnFormulas ? new HashSet<CellAddress>() : null;
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            if (sheet.FormulaCellCount == 0)
                continue;

            List<FormulaPattern>? formulas = checkInconsistentFormulas
                ? new List<FormulaPattern>(sheet.FormulaCellCount)
                : null;

            foreach (var (address, cell) in EnumerateFormulaIssueCandidates(sheet))
            {
                if (cell.IgnoreFormulaError)
                    continue;

                if (checkInconsistentCalculatedColumnFormulas &&
                    IsInconsistentCalculatedColumnFormula(sheet, address, cell))
                {
                    flaggedCalculatedColumnFormulas?.Add(address);
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        InconsistentCalculatedColumnFormulaErrorCode,
                        cell.FormulaText is null ? null : "=" + cell.FormulaText,
                        "The formula is inconsistent with the table calculated column formula.");
                }

                if (checkBlankReferences && FormulaRefersToBlankCells(workbook, sheet.Id, cell))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        FormulaRefersToBlankCellsErrorCode,
                        cell.FormulaText is null ? null : "=" + cell.FormulaText,
                        "The formula refers to one or more blank cells.");
                }

                if (formulas is not null &&
                    flaggedCalculatedColumnFormulas?.Contains(address) != true &&
                    !string.IsNullOrWhiteSpace(cell.FormulaText))
                {
                    formulas.Add(new FormulaPattern(address, cell.FormulaText!, NormalizeFormulaPattern(address, cell.FormulaText!)));
                }

                if (checkOmittedAdjacentCells && FormulaOmitsAdjacentCells(workbook, sheet.Id, cell))
                {
                    yield return new FormulaErrorIssue(
                        sheet.Id,
                        sheet.Name,
                        address,
                        address.ToA1(),
                        FormulaOmitsAdjacentCellsErrorCode,
                        cell.FormulaText is null ? null : "=" + cell.FormulaText,
                        "The formula omits adjacent cells in the region.");
                }

                if (checkUnlockedFormulaCells && IsUnlockedFormulaCell(workbook, cell))
                {
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

            if (formulas is null || formulas.Count == 0 || flaggedInconsistentFormulas is null)
                continue;

            foreach (var issue in FindInconsistentFormulaRuns(sheet, formulas.GroupBy(item => item.Address.Row), flaggedInconsistentFormulas))
                yield return issue;

            foreach (var issue in FindInconsistentFormulaRuns(sheet, formulas.GroupBy(item => item.Address.Col), flaggedInconsistentFormulas))
                yield return issue;
        }
    }

    private static IEnumerable<FormulaErrorIssue> FindInconsistentFormulaIssues(Workbook workbook, SheetId? sheetId)
    {
        var flagged = new HashSet<CellAddress>();
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            if (sheet.FormulaCellCount == 0)
                continue;

            var formulas = new List<FormulaPattern>(sheet.FormulaCellCount);
            foreach (var (address, cell) in EnumerateFormulaIssueCandidates(sheet))
            {
                if (cell.IgnoreFormulaError || string.IsNullOrWhiteSpace(cell.FormulaText))
                    continue;

                formulas.Add(new FormulaPattern(address, cell.FormulaText!, NormalizeFormulaPattern(address, cell.FormulaText!)));
            }

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

    private static bool IsInconsistentCalculatedColumnFormula(Workbook workbook, SheetId sheetId, CellAddress address, Cell cell) =>
        workbook.GetSheet(sheetId) is { } sheet &&
        IsInconsistentCalculatedColumnFormula(sheet, address, cell);

    private static bool IsInconsistentCalculatedColumnFormula(Sheet sheet, CellAddress address, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        return TryGetCalculatedColumnFormula(sheet, address, out var calculatedColumnFormula) &&
               !FormulaTextsMatch(cell.FormulaText, calculatedColumnFormula);
    }

    private static bool TryGetCalculatedColumnFormula(
        Sheet sheet,
        CellAddress address,
        out string calculatedColumnFormula)
    {
        calculatedColumnFormula = string.Empty;
        foreach (var table in sheet.StructuredTables)
        {
            if (!TryGetTableDataBodyRows(table, out var startRow, out var endRow) ||
                address.Row < startRow ||
                address.Row > endRow ||
                address.Col < table.Range.Start.Col ||
                address.Col > table.Range.End.Col)
            {
                continue;
            }

            var columnIndex = (int)(address.Col - table.Range.Start.Col);
            if (columnIndex < 0 || columnIndex >= table.Columns.Count)
                continue;

            calculatedColumnFormula = table.Columns[columnIndex].CalculatedColumnFormula ?? string.Empty;
            return !string.IsNullOrWhiteSpace(calculatedColumnFormula);
        }

        return false;
    }

    private static bool TryGetTableDataBodyRows(StructuredTableModel table, out uint startRow, out uint endRow)
    {
        startRow = 0;
        endRow = 0;
        if (table.Range.End.Row < table.Range.Start.Row || table.Range.End.Col < table.Range.Start.Col)
            return false;

        var rowCount = (int)(table.Range.End.Row - table.Range.Start.Row + 1);
        var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var remainingRows = rowCount - headerRows;
        var totalsRows = table.TotalsRowShown
            ? Math.Clamp(table.TotalsRowCount ?? 1, 0, remainingRows)
            : 0;
        var dataRows = rowCount - headerRows - totalsRows;
        if (dataRows <= 0)
            return false;

        startRow = table.Range.Start.Row + (uint)headerRows;
        endRow = startRow + (uint)dataRows - 1;
        return true;
    }

    private static bool FormulaTextsMatch(string actualFormula, string expectedFormula) =>
        string.Equals(
            NormalizeFormulaTextForComparison(actualFormula),
            NormalizeFormulaTextForComparison(expectedFormula),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFormulaTextForComparison(string formulaText)
    {
        var normalized = formulaText.Trim();
        if (normalized.StartsWith("=", StringComparison.Ordinal))
            normalized = normalized[1..].TrimStart();

        return normalized.TrimEnd();
    }

    private sealed record FormulaPattern(CellAddress Address, string FormulaText, string Pattern);

    private static IEnumerable<FormulaErrorIssue> FindFormulaOmitsAdjacentCellsIssues(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            foreach (var (address, cell) in EnumerateFormulaIssueCandidates(sheet))
            {
                if (cell.IgnoreFormulaError || !FormulaOmitsAdjacentCells(workbook, sheet.Id, cell))
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

            foreach (var (address, cell) in EnumerateFormulaIssueCandidates(sheet))
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

    private static IEnumerable<(CellAddress Address, Cell Cell)> EnumerateFormulaIssueCandidates(Sheet sheet)
    {
        foreach (var address in sheet.EnumerateFormulaCells())
        {
            if (sheet.GetCell(address) is { } cell)
                yield return (address, cell);
        }
    }

    private static bool IsUnlockedFormulaCell(Workbook workbook, Cell cell) =>
        cell.HasFormula && !workbook.GetStyle(cell.StyleId).Locked;

    private static bool FormulaOmitsAdjacentCells(Workbook workbook, SheetId sheetId, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        if (!ContainsOmittedAdjacentCellsAggregateFunction(cell.FormulaText))
            return false;

        foreach (var ranges in ExtractAggregateRangeGroups(sheetId, cell.FormulaText))
        {
            var sameSheetRanges = ranges
                .Where(range => range.Start.Sheet == range.End.Sheet)
                .ToList();

            foreach (var range in sameSheetRanges)
            {
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

            if (HasOmittedValuesBetweenAggregateArguments(workbook, sameSheetRanges))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOmittedAdjacentCellsAggregateFunction(string formulaText) =>
        OmittedAdjacentCellsAggregateFunctions.Any(functionName =>
            formulaText.Contains(functionName, StringComparison.OrdinalIgnoreCase));

    private static bool HasOmittedValuesBetweenAggregateArguments(Workbook workbook, IReadOnlyList<GridRange> ranges)
    {
        foreach (var group in ranges.GroupBy(range => (range.Start.Sheet, range.Start.Col)))
        {
            var verticalRanges = group
                .Where(IsSingleColumnRange)
                .OrderBy(range => range.Start.Row)
                .ToList();
            if (HasOmittedValuesInLine(
                    workbook,
                    verticalRanges,
                    valueSelector: row => new CellAddress(group.Key.Sheet, row, group.Key.Col),
                    startSelector: range => range.Start.Row,
                    endSelector: range => range.End.Row))
            {
                return true;
            }
        }

        foreach (var group in ranges.GroupBy(range => (range.Start.Sheet, range.Start.Row)))
        {
            var horizontalRanges = group
                .Where(IsSingleRowRange)
                .OrderBy(range => range.Start.Col)
                .ToList();
            if (HasOmittedValuesInLine(
                    workbook,
                    horizontalRanges,
                    valueSelector: col => new CellAddress(group.Key.Sheet, group.Key.Row, col),
                    startSelector: range => range.Start.Col,
                    endSelector: range => range.End.Col))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOmittedValuesInLine(
        Workbook workbook,
        IReadOnlyList<GridRange> ranges,
        Func<uint, CellAddress> valueSelector,
        Func<GridRange, uint> startSelector,
        Func<GridRange, uint> endSelector)
    {
        if (ranges.Count < 2)
            return false;

        var min = ranges.Min(startSelector);
        var max = ranges.Max(endSelector);
        for (var index = min; index <= max; index++)
        {
            if (ranges.Any(range => startSelector(range) <= index && index <= endSelector(range)))
                continue;

            if (HasValueAt(workbook, valueSelector(index)))
                return true;
        }

        return false;
    }

    private static bool IsSingleColumnRange(GridRange range) =>
        range.Start.Col == range.End.Col;

    private static bool IsSingleRowRange(GridRange range) =>
        range.Start.Row == range.End.Row;

    private static IEnumerable<IReadOnlyList<GridRange>> ExtractAggregateRangeGroups(SheetId sheetId, string formulaText)
    {
        foreach (Match aggregateMatch in Regex.Matches(
                     formulaText,
                     $@"\b(?:{OmittedAdjacentCellsAggregateFunctionPattern})\s*\((?<args>[^)]*)\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var ranges = new List<GridRange>();
            foreach (var token in aggregateMatch.Groups["args"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseLocalRange(sheetId, token, out var range))
                    ranges.Add(range);
            }

            if (ranges.Count > 0)
                yield return ranges;
        }
    }

    private static bool TryParseLocalRange(SheetId sheetId, string token, out GridRange range)
    {
        range = default;
        var match = Regex.Match(
            token,
            @"^(?<start>\$?[A-Za-z]{1,3}\$?[0-9]{1,7})(?::(?<end>\$?[A-Za-z]{1,3}\$?[0-9]{1,7}))?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var start = ParseLocalAddress(sheetId, match.Groups["start"].Value);
        var end = match.Groups["end"].Success
            ? ParseLocalAddress(sheetId, match.Groups["end"].Value)
            : start;
        range = NormalizeRange(start, end);
        return true;
    }

    private static GridRange NormalizeRange(CellAddress start, CellAddress end)
    {
        var normalizedStart = new CellAddress(
            start.Sheet,
            Math.Min(start.Row, end.Row),
            Math.Min(start.Col, end.Col));
        var normalizedEnd = new CellAddress(
            start.Sheet,
            Math.Max(start.Row, end.Row),
            Math.Max(start.Col, end.Col));
        return new GridRange(normalizedStart, normalizedEnd);
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

    private static string NormalizeFormulaPattern(CellAddress address, string formulaText)
    {
        StringBuilder? builder = null;
        var appendStart = 0;

        for (var index = 0; index < formulaText.Length; index++)
        {
            if (!IsFormulaReferenceBoundaryBefore(formulaText, index) ||
                !TryReadFormulaReference(formulaText, index, out var end, out var row, out var col))
            {
                continue;
            }

            builder ??= new StringBuilder(formulaText.Length + 8);
            builder.Append(formulaText, appendStart, index - appendStart);
            builder.Append("R[");
            builder.Append((int)row - (int)address.Row);
            builder.Append("]C[");
            builder.Append((int)col - (int)address.Col);
            builder.Append(']');

            appendStart = end;
            index = end - 1;
        }

        if (builder is null)
            return formulaText;

        builder.Append(formulaText, appendStart, formulaText.Length - appendStart);
        return builder.ToString();
    }

    private static bool TryReadFormulaReference(
        string formulaText,
        int start,
        out int end,
        out uint row,
        out uint col)
    {
        end = start;
        row = 0;
        col = 0;

        var index = start;
        if (index < formulaText.Length && formulaText[index] == '$')
            index++;

        var letterCount = 0;
        while (index < formulaText.Length &&
               letterCount < 3 &&
               TryNormalizeFormulaColumnLetter(formulaText[index], out var letter))
        {
            col = col * 26 + (uint)(letter - 'A' + 1);
            letterCount++;
            index++;
        }

        if (letterCount == 0)
            return false;

        if (index < formulaText.Length && IsAsciiLetter(formulaText[index]))
            return false;

        if (index < formulaText.Length && formulaText[index] == '$')
            index++;

        var digitCount = 0;
        while (index < formulaText.Length && digitCount < 7 && formulaText[index] is >= '0' and <= '9')
        {
            row = (row * 10) + (uint)(formulaText[index] - '0');
            digitCount++;
            index++;
        }

        if (digitCount == 0 || row == 0)
            return false;

        if (index < formulaText.Length && formulaText[index] is >= '0' and <= '9')
            return false;

        if (!IsFormulaReferenceBoundaryAfter(formulaText, index))
            return false;

        end = index;
        return true;
    }

    private static bool IsFormulaReferenceBoundaryBefore(string text, int index) =>
        index == 0 || !IsAsciiLetterDigitOrUnderscore(text[index - 1]);

    private static bool IsFormulaReferenceBoundaryAfter(string text, int index) =>
        index >= text.Length || !IsAsciiLetterDigitOrUnderscore(text[index]);

    private static bool TryNormalizeFormulaColumnLetter(char value, out char letter)
    {
        letter = value is >= 'a' and <= 'z'
            ? (char)(value - ('a' - 'A'))
            : value;

        return letter is >= 'A' and <= 'Z';
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterDigitOrUnderscore(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';

    private static bool FormulaRefersToBlankCells(Workbook workbook, SheetId sheetId, Cell cell)
    {
        if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
            return false;

        return HasAnyBlankPrecedent(workbook, sheetId, cell.FormulaText);
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

    private static bool IsFormulaStoredAsText(Cell cell) =>
        !cell.HasFormula &&
        cell.Value is TextValue text &&
        IsFormulaTextLiteral(text.Value);

    private static bool IsFormulaTextLiteral(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 1 && trimmed[0] == '=';
    }

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
