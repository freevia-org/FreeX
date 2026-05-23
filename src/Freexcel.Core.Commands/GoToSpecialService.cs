using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
            return SelectionRangeService.GetCurrentRegion(sheet, activeCell ?? range.Start)?.AllCells().ToList() ?? [];

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
                case GoToSpecialKind.DataValidation when sheet.DataValidations.Any(rule => rule.AppliesTo.Contains(address)):
                    result.Add(address);
                    break;
                case GoToSpecialKind.ConditionalFormats when sheet.ConditionalFormats.Any(rule => rule.AppliesTo.Contains(address)):
                    result.Add(address);
                    break;
            }
        }

        return result;
    }

    private static bool MatchesValueType(ScalarValue value, GoToSpecialValueTypes valueTypes) =>
        value switch
        {
            NumberValue or DateTimeValue => valueTypes.HasFlag(GoToSpecialValueTypes.Numbers),
            TextValue => valueTypes.HasFlag(GoToSpecialValueTypes.Text),
            BoolValue => valueTypes.HasFlag(GoToSpecialValueTypes.Logicals),
            ErrorValue => valueTypes.HasFlag(GoToSpecialValueTypes.Errors),
            _ => false
        };

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
        foreach (var address in range.AllCells())
        {
            foreach (var dependent in FormulaAuditingService.GetDirectDependents(workbook, address))
                if (dependent.Sheet == sheet.Id && !result.Contains(dependent))
                    result.Add(dependent);
        }

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
