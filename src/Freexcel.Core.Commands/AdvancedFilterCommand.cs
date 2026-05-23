using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AdvancedFilterCommand : IWorkbookCommand
{
    private readonly GridRange _listRange;
    private readonly GridRange _criteriaRange;
    private readonly CellAddress? _copyTo;
    private readonly GridRange? _copyToRange;
    private readonly bool _uniqueRecordsOnly;
    private HashSet<uint>? _previousFilterHiddenRows;
    private List<(CellAddress Address, Cell? OldCell)>? _copySnapshot;

    public string Label => "Advanced Filter";

    public AdvancedFilterCommand(
        GridRange ListRange,
        GridRange CriteriaRange,
        CellAddress? CopyTo,
        bool UniqueRecordsOnly,
        GridRange? CopyToRange = null)
    {
        _listRange = ListRange;
        _criteriaRange = CriteriaRange;
        _copyTo = CopyTo;
        _copyToRange = CopyToRange;
        _uniqueRecordsOnly = UniqueRecordsOnly;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_listRange.Start.Sheet != _listRange.End.Sheet ||
            _criteriaRange.Start.Sheet != _criteriaRange.End.Sheet)
            return new CommandOutcome(false, "Advanced Filter list and criteria ranges must each stay on one sheet.");

        var sheet = ctx.GetSheet(_listRange.Start.Sheet);
        var criteriaSheet = ctx.GetSheet(_criteriaRange.Start.Sheet);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var headers = BuildHeaderMap(sheet, _listRange);
        var criteria = BuildCriteriaRows(criteriaSheet, _criteriaRange, headers);
        if (criteria.Error is not null)
            return new CommandOutcome(false, criteria.Error);
        if (criteria.Rows.Count == 0)
            return new CommandOutcome(false, "Advanced Filter requires at least one criterion.");

        var matches = MatchingRows(sheet, criteria.Rows).ToList();
        if (_uniqueRecordsOnly)
            matches = UniqueRows(sheet, matches).ToList();

        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
        _copySnapshot = null;

        if (_copyTo is null)
        {
            for (var row = _listRange.Start.Row + 1; row <= _listRange.End.Row; row++)
                sheet.FilterHiddenRows.Remove(row);
            foreach (var row in Enumerable.Range((int)_listRange.Start.Row + 1, (int)(_listRange.End.Row - _listRange.Start.Row)).Select(r => (uint)r))
                if (!matches.Contains(row))
                    sheet.FilterHiddenRows.Add(row);
            return new CommandOutcome(true);
        }

        if (_copyTo.Value.Sheet != sheet.Id)
            return new CommandOutcome(false, "Copy destination must be on the filtered sheet.");

        CopyMatches(sheet, matches);
        return new CommandOutcome(true, AffectedCells: [_copyTo.Value]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_listRange.Start.Sheet);
        if (_previousFilterHiddenRows is not null)
        {
            sheet.FilterHiddenRows.Clear();
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
        }

        if (_copySnapshot is not null)
        {
            foreach (var (address, oldCell) in _copySnapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell.Clone());
            }
        }
    }

    private Dictionary<string, uint> BuildHeaderMap(Sheet sheet, GridRange range)
    {
        var headers = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var text = FilterValueFormatter.ToText(sheet.GetValue(range.Start.Row, col));
            if (text.Length > 0 && !headers.ContainsKey(text))
                headers[text] = col;
        }
        return headers;
    }

    private (List<List<(uint Col, IFilterCriterion Criterion)>> Rows, string? Error) BuildCriteriaRows(
        Sheet sheet,
        GridRange criteriaRange,
        Dictionary<string, uint> headers)
    {
        var result = new List<List<(uint Col, IFilterCriterion Criterion)>>();
        for (var row = criteriaRange.Start.Row + 1; row <= criteriaRange.End.Row; row++)
        {
            var criteriaRow = new List<(uint Col, IFilterCriterion Criterion)>();
            for (var col = criteriaRange.Start.Col; col <= criteriaRange.End.Col; col++)
            {
                var headerText = FilterValueFormatter.ToText(sheet.GetValue(criteriaRange.Start.Row, col));
                if (string.IsNullOrWhiteSpace(headerText))
                    continue;
                if (!headers.TryGetValue(headerText, out var listCol))
                    return ([], $"Criteria header '{headerText}' was not found in the list range.");

                var criteriaText = FilterValueFormatter.ToText(sheet.GetValue(row, col));
                if (criteriaText.Length == 0)
                    continue;

                criteriaRow.Add((listCol, CreateCriterion(criteriaText)));
            }

            if (criteriaRow.Count > 0)
                result.Add(criteriaRow);
        }
        return (result, null);
    }

    private IEnumerable<uint> MatchingRows(Sheet sheet, IReadOnlyList<List<(uint Col, IFilterCriterion Criterion)>> criteriaRows)
    {
        for (var row = _listRange.Start.Row + 1; row <= _listRange.End.Row; row++)
        {
            foreach (var criteriaRow in criteriaRows)
            {
                var matches = true;
                foreach (var (col, criterion) in criteriaRow)
                {
                    if (!criterion.Matches(sheet.GetValue(row, col)))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    yield return row;
                    break;
                }
            }
        }
    }

    private IEnumerable<uint> UniqueRows(Sheet sheet, IReadOnlyList<uint> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join('\u001f', Enumerable
                .Range((int)_listRange.Start.Col, (int)_listRange.ColCount)
                .Select(col => FilterValueFormatter.ToText(sheet.GetValue(row, (uint)col))));
            if (seen.Add(key))
                yield return row;
        }
    }

    private void CopyMatches(Sheet sheet, IReadOnlyList<uint> rows)
    {
        _copySnapshot = [];
        var outputColumns = ResolveCopyOutputColumns(sheet);
        var outputWidth = outputColumns is null ? _listRange.ColCount : (uint)outputColumns.Count;
        var clearWidth = Math.Max(_listRange.ColCount, outputWidth);
        var outputRowCount = 1 + rows.Count;
        var rowsToReplace = Math.Max(outputRowCount, CountExistingDestinationRows(sheet, clearWidth));
        for (uint r = 0; r < rowsToReplace; r++)
        {
            for (uint c = 0; c < clearWidth; c++)
            {
                var target = new CellAddress(sheet.Id, _copyTo!.Value.Row + r, _copyTo.Value.Col + c);
                _copySnapshot.Add((target, sheet.GetCell(target)?.Clone()));
                if (r >= outputRowCount || c >= outputWidth)
                {
                    sheet.ClearCell(target);
                    continue;
                }

                var sourceRow = r == 0 ? _listRange.Start.Row : rows[(int)r - 1];
                var sourceCol = outputColumns is null ? _listRange.Start.Col + c : outputColumns[(int)c];
                var source = new CellAddress(sheet.Id, sourceRow, sourceCol);
                var sourceCell = sheet.GetCell(source)?.Clone()
                    ?? Cell.FromValue(sheet.GetValue(source.Row, source.Col));
                sheet.SetCell(target, sourceCell);
            }
        }
    }

    private uint CountExistingDestinationRows(Sheet sheet, uint outputWidth)
    {
        if (_copyTo is null)
            return 0;

        if (sheet.GetUsedRange() is not { } usedRange || _copyTo.Value.Row > usedRange.End.Row)
            return 0;

        uint count = 0;
        for (var row = _copyTo.Value.Row; row <= usedRange.End.Row; row++)
        {
            var hasOutputCell = false;
            for (uint colOffset = 0; colOffset < outputWidth; colOffset++)
            {
                if (sheet.GetCell(row, _copyTo.Value.Col + colOffset) is null)
                    continue;

                hasOutputCell = true;
                break;
            }

            if (!hasOutputCell)
                break;

            count++;
        }

        return count;
    }

    private IReadOnlyList<uint>? ResolveCopyOutputColumns(Sheet sheet)
    {
        if (_copyToRange is not { } range || range.Start.Row != range.End.Row)
            return null;

        var headers = BuildHeaderMap(sheet, _listRange);
        var selectedColumns = new List<uint>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var headerText = FilterValueFormatter.ToText(sheet.GetValue(range.Start.Row, col));
            if (string.IsNullOrWhiteSpace(headerText))
                return null;
            if (!headers.TryGetValue(headerText, out var sourceCol))
                return null;

            selectedColumns.Add(sourceCol);
        }

        return selectedColumns.Count == 0 ? null : selectedColumns;
    }

    private static IFilterCriterion CreateCriterion(string criteriaText)
    {
        if (FilterInputParser.TryParseCriterion(criteriaText, out var parsed, out _))
            return parsed!;
        if (criteriaText.StartsWith('='))
            return new TextEqualsFilterCriterion(criteriaText[1..]);
        return new TextEqualsFilterCriterion(criteriaText);
    }
}
