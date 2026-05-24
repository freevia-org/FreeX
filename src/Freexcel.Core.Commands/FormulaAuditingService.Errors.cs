using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Commands;

public static partial class FormulaAuditingService
{
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
