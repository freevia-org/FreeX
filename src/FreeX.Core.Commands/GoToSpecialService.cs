using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum GoToSpecialKind
{
    Blanks,
    Constants,
    Formulas,
    Comments,
    DataValidation,
    VisibleCellsOnly,
    RowDifferences,
    ColumnDifferences,
    CurrentRegion,
    LastCell,
    ConditionalFormats,
    Objects,
    Precedents,
    Dependents
}

[Flags]
public enum GoToSpecialValueTypes
{
    None = 0,
    Numbers = 1,
    Text = 2,
    Logicals = 4,
    Errors = 8,
    All = Numbers | Text | Logicals | Errors
}

public sealed record GoToSpecialOptions(GoToSpecialValueTypes ValueTypes = GoToSpecialValueTypes.All);

public static class GoToSpecialService
{
    private const int MinimumRuleRangesForIndex = 8;
    private const long MaximumIndexedRuleCells = 250_000;

    public static IReadOnlyList<CellAddress> Find(
        Sheet sheet,
        GridRange range,
        GoToSpecialKind kind,
        CellAddress? activeCell = null,
        GoToSpecialOptions? options = null)
        => Find(null, sheet, range, kind, activeCell, options);

    public static IReadOnlyList<CellAddress> Find(
        Workbook? workbook,
        Sheet sheet,
        GridRange range,
        GoToSpecialKind kind,
        CellAddress? activeCell = null,
        GoToSpecialOptions? options = null)
    {
        options ??= new GoToSpecialOptions();

        if (kind == GoToSpecialKind.CurrentRegion)
            return SelectionRangeService.GetCurrentRegion(sheet, activeCell ?? range.Start) is { } currentRegion
                ? MaterializeCells(currentRegion)
                : [];

        if (kind == GoToSpecialKind.LastCell)
            return sheet.GetUsedRange() is { } usedRange ? [usedRange.End] : [];

        if (kind == GoToSpecialKind.Objects)
            return FindObjects(sheet, range);

        if (kind == GoToSpecialKind.Precedents)
            return workbook is null ? [] : FindPrecedents(workbook, sheet, range);

        if (kind == GoToSpecialKind.Dependents)
            return workbook is null ? [] : FindDependents(workbook, sheet, range);

        var result = new List<CellAddress>();
        if (kind == GoToSpecialKind.RowDifferences)
            return FindRowDifferences(sheet, range);
        if (kind == GoToSpecialKind.ColumnDifferences)
            return FindColumnDifferences(sheet, range);
        if (kind == GoToSpecialKind.DataValidation)
            return FindDataValidations(sheet.DataValidations, range);
        if (kind == GoToSpecialKind.ConditionalFormats)
            return FindConditionalFormats(sheet.ConditionalFormats, range);

        foreach (var address in range.AllCells())
        {
            if (kind == GoToSpecialKind.VisibleCellsOnly)
            {
                if (!sheet.IsRowEffectivelyHidden(address.Row) &&
                    !sheet.IsColEffectivelyHidden(address.Col))
                {
                    result.Add(address);
                }
                continue;
            }

            var cell = sheet.GetCell(address);
            switch (kind)
            {
                case GoToSpecialKind.Blanks when cell is null || cell.Value is BlankValue:
                    result.Add(address);
                    break;
                case GoToSpecialKind.Constants when cell is { HasFormula: false } &&
                    cell.Value is not BlankValue &&
                    MatchesValueType(cell.Value, options.ValueTypes):
                    result.Add(address);
                    break;
                case GoToSpecialKind.Formulas when cell?.HasFormula == true &&
                    MatchesValueType(cell.Value, options.ValueTypes):
                    result.Add(address);
                    break;
                case GoToSpecialKind.Comments when sheet.Comments.ContainsKey(address) || sheet.ThreadedComments.ContainsKey(address):
                    result.Add(address);
                    break;
            }
        }

        return result;
    }

    private static bool MatchesValueType(ScalarValue value, GoToSpecialValueTypes valueTypes) =>
        value switch
        {
            NumberValue or DateTimeValue => (valueTypes & GoToSpecialValueTypes.Numbers) != 0,
            TextValue => (valueTypes & GoToSpecialValueTypes.Text) != 0,
            BoolValue => (valueTypes & GoToSpecialValueTypes.Logicals) != 0,
            ErrorValue => (valueTypes & GoToSpecialValueTypes.Errors) != 0,
            _ => false
        };

    private static List<CellAddress> MaterializeCells(GridRange range)
    {
        var capacity = range.CellCount <= int.MaxValue ? (int)range.CellCount : 0;
        var cells = capacity > 0 ? new List<CellAddress>(capacity) : [];
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                cells.Add(new CellAddress(range.Start.Sheet, row, col));
        }

        return cells;
    }

    private static bool HasConditionalFormatAt(List<ConditionalFormat> rules, CellAddress address)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].AppliesTo.Contains(address))
                return true;
        }

        return false;
    }

    private static bool HasDataValidationAt(List<DataValidation> rules, CellAddress address)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.AppliesTo.Contains(address))
                return true;

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                if (additionalRanges[rangeIndex].Contains(address))
                    return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<CellAddress> FindConditionalFormats(List<ConditionalFormat> rules, GridRange range)
    {
        if (rules.Count == 0)
            return [];

        var (coversRange, coveredCells, rangeCount) = AnalyzeConditionalFormatRanges(rules, range);
        if (coversRange)
            return MaterializeCells(range);

        if (ShouldIndexRuleRanges(rangeCount, coveredCells))
        {
            var matches = BuildConditionalFormatAddressSet(rules, range, coveredCells);
            return MaterializeMatches(range, matches);
        }

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasConditionalFormatAt(rules, address))
                result.Add(address);
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindDataValidations(List<DataValidation> rules, GridRange range)
    {
        if (rules.Count == 0)
            return [];

        var (coversRange, coveredCells, rangeCount) = AnalyzeDataValidationRanges(rules, range);
        if (coversRange)
            return MaterializeCells(range);

        if (ShouldIndexRuleRanges(rangeCount, coveredCells))
        {
            var matches = BuildDataValidationAddressSet(rules, range, coveredCells);
            return MaterializeMatches(range, matches);
        }

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasDataValidationAt(rules, address))
                result.Add(address);
        }

        return result;
    }

    private static bool ShouldIndexRuleRanges(int rangeCount, long coveredCells) =>
        rangeCount >= MinimumRuleRangesForIndex &&
        coveredCells > 0 &&
        coveredCells <= MaximumIndexedRuleCells;

    private static (bool CoversRange, long CoveredCells, int RangeCount) AnalyzeConditionalFormatRanges(
        List<ConditionalFormat> rules,
        GridRange searchRange)
    {
        long coveredCells = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            var ruleRange = rules[i].AppliesTo;
            if (ContainsRange(ruleRange, searchRange))
                return (true, coveredCells, i + 1);

            if (TryIntersect(ruleRange, searchRange, out var intersection))
                coveredCells += intersection.CellCount;
        }

        return (false, coveredCells, rules.Count);
    }

    private static (bool CoversRange, long CoveredCells, int RangeCount) AnalyzeDataValidationRanges(
        List<DataValidation> rules,
        GridRange searchRange)
    {
        long coveredCells = 0;
        var rangeCount = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            rangeCount++;
            if (ContainsRange(rule.AppliesTo, searchRange))
                return (true, coveredCells, rangeCount);

            if (TryIntersect(rule.AppliesTo, searchRange, out var intersection))
                coveredCells += intersection.CellCount;

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                rangeCount++;
                var additionalRange = additionalRanges[rangeIndex];
                if (ContainsRange(additionalRange, searchRange))
                    return (true, coveredCells, rangeCount);

                if (TryIntersect(additionalRange, searchRange, out intersection))
                    coveredCells += intersection.CellCount;
            }
        }

        return (false, coveredCells, rangeCount);
    }

    private static HashSet<(uint Row, uint Col)> BuildConditionalFormatAddressSet(
        List<ConditionalFormat> rules,
        GridRange searchRange,
        long coveredCells)
    {
        var matches = new HashSet<(uint Row, uint Col)>(GetHashSetCapacity(coveredCells));
        for (var i = 0; i < rules.Count; i++)
        {
            if (TryIntersect(rules[i].AppliesTo, searchRange, out var intersection))
                AddCells(matches, intersection);
        }

        return matches;
    }

    private static HashSet<(uint Row, uint Col)> BuildDataValidationAddressSet(
        List<DataValidation> rules,
        GridRange searchRange,
        long coveredCells)
    {
        var matches = new HashSet<(uint Row, uint Col)>(GetHashSetCapacity(coveredCells));
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (TryIntersect(rule.AppliesTo, searchRange, out var intersection))
                AddCells(matches, intersection);

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                if (TryIntersect(additionalRanges[rangeIndex], searchRange, out intersection))
                    AddCells(matches, intersection);
            }
        }

        return matches;
    }

    private static int GetHashSetCapacity(long coveredCells) =>
        coveredCells <= int.MaxValue ? (int)coveredCells : 0;

    private static void AddCells(HashSet<(uint Row, uint Col)> matches, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                matches.Add((row, col));
        }
    }

    private static IReadOnlyList<CellAddress> MaterializeMatches(
        GridRange range,
        HashSet<(uint Row, uint Col)> matches)
    {
        if (matches.Count == 0)
            return [];

        var result = new List<CellAddress>(matches.Count);
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (matches.Contains((row, col)))
                    result.Add(new CellAddress(range.Start.Sheet, row, col));
            }
        }

        return result;
    }

    private static bool ContainsRange(GridRange outer, GridRange inner) =>
        outer.Start.Sheet == inner.Start.Sheet &&
        outer.Start.Row <= inner.Start.Row &&
        outer.End.Row >= inner.End.Row &&
        outer.Start.Col <= inner.Start.Col &&
        outer.End.Col >= inner.End.Col;

    private static bool TryIntersect(GridRange first, GridRange second, out GridRange intersection)
    {
        if (!first.Overlaps(second))
        {
            intersection = default;
            return false;
        }

        intersection = new GridRange(
            new CellAddress(
                first.Start.Sheet,
                Math.Max(first.Start.Row, second.Start.Row),
                Math.Max(first.Start.Col, second.Start.Col)),
            new CellAddress(
                first.Start.Sheet,
                Math.Min(first.End.Row, second.End.Row),
                Math.Min(first.End.Col, second.End.Col)));
        return true;
    }

    private static IReadOnlyList<CellAddress> FindObjects(Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        foreach (var chart in sheet.Charts)
            AddIfInRange(result, range, chart.DataRange.Start);
        foreach (var shape in sheet.DrawingShapes)
            AddIfInRange(result, range, shape.Anchor);
        foreach (var picture in sheet.Pictures)
            AddIfInRange(result, range, picture.Anchor);
        foreach (var textBox in sheet.TextBoxes)
            AddIfInRange(result, range, textBox.Anchor);

        return result;
    }

    private static IReadOnlyList<CellAddress> FindPrecedents(Workbook workbook, Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            foreach (var precedent in FormulaAuditingService.GetDirectPrecedents(workbook, address))
                if (precedent.Sheet == sheet.Id && !result.Contains(precedent))
                    result.Add(precedent);
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindDependents(Workbook workbook, Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        foreach (var dependent in FormulaAuditingService.GetDirectDependents(workbook, range))
            if (dependent.Sheet == sheet.Id)
                result.Add(dependent);

        return result;
    }

    private static void AddIfInRange(List<CellAddress> result, GridRange range, CellAddress address)
    {
        if (range.Contains(address) && !result.Contains(address))
            result.Add(address);
    }

    private static IReadOnlyList<CellAddress> FindRowDifferences(Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var firstValue = sheet.GetCell(row, range.Start.Col)?.Value ?? BlankValue.Instance;
            for (var col = range.Start.Col + 1; col <= range.End.Col; col++)
            {
                var address = new CellAddress(range.Start.Sheet, row, col);
                var value = sheet.GetCell(address)?.Value ?? BlankValue.Instance;
                if (!ScalarEquals(firstValue, value))
                    result.Add(address);
            }
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindColumnDifferences(Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var firstValue = sheet.GetCell(range.Start.Row, col)?.Value ?? BlankValue.Instance;
            for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
            {
                var address = new CellAddress(range.Start.Sheet, row, col);
                var value = sheet.GetCell(address)?.Value ?? BlankValue.Instance;
                if (!ScalarEquals(firstValue, value))
                    result.Add(address);
            }
        }

        return result;
    }

    private static bool ScalarEquals(ScalarValue a, ScalarValue b) =>
        (a, b) switch
        {
            (TextValue ta, TextValue tb) => string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase),
            (NumberValue na, NumberValue nb) => na.Value.Equals(nb.Value),
            (DateTimeValue da, DateTimeValue db) => da.Value.Equals(db.Value),
            (BoolValue ba, BoolValue bb) => ba.Value == bb.Value,
            (BlankValue, BlankValue) => true,
            _ => false
        };
}
