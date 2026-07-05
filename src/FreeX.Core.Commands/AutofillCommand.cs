using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Fills a range by repeating or continuing the series of <paramref name="sourceRange"/>.
/// Formulas have relative cell references incremented by the fill offset. When
/// <paramref name="fillRange"/> is a sub-range of <paramref name="sourceRange"/> (the user dragged
/// the fill handle inward instead of extending it), the cells beyond the shrunk boundary are
/// cleared instead, matching Excel.
/// </summary>
public sealed class AutofillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private readonly bool _ctrlHeld;
    private List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Autofill";

    /// <param name="ctrlHeld">
    /// True when the user held Ctrl while releasing the fill-handle drag. Excel uses Ctrl to flip
    /// the fill handle's default behavior: a detected series (2+ source cells, or any text/list
    /// series) becomes a plain copy of the last value, while a single plain number/date cell
    /// (which otherwise just copies) becomes an incrementing series instead.
    /// </param>
    public AutofillCommand(SheetId sheetId, GridRange sourceRange, GridRange fillRange, bool ctrlHeld = false)
    {
        _sheetId     = sheetId;
        _sourceRange = sourceRange;
        _fillRange   = fillRange;
        _ctrlHeld    = ctrlHeld;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        if (_sourceRange.Contains(_fillRange) && _fillRange != _sourceRange)
            return ApplyInwardClear(ctx, sheet);

        if (!TryGetFillPlan(out var plan))
            return new CommandOutcome(false, "The autofill range must be adjacent to the source range and aligned by row or column.");

        // Excel refuses to fill across a merged region: the merge's non-anchor cells must never
        // receive independent content, and a fill that only partially covers a merge would leave
        // the merge's data model out of sync (mirrors MoveRangeCommand/SortCommand's merge guard).
        if (sheet.MergedRegions.Any(region => _fillRange.Overlaps(region) || _sourceRange.Overlaps(region)))
            return new CommandOutcome(false, "Cannot autofill a range that intersects merged cells.");

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        var sourceAddr = GetSourceEdgeAddress(plan);
        var sourceCell = sheet.GetCell(sourceAddr);
        var sourceHasFormula = sourceCell is { HasFormula: true, FormulaText: not null };
        var sourceLength = plan.Axis == FillAxis.Vertical ? (int)_sourceRange.RowCount : (int)_sourceRange.ColCount;
        var naturalScalarSeries = sourceHasFormula ? null : TryCreateScalarSeries(sheet, plan);
        var naturalListSeries = sourceHasFormula || naturalScalarSeries is not null ? null : TryCreateListSeries(sheet, plan);
        // Ctrl flips the natural default: a detected series (scalar trend, or any text/list
        // series) becomes a plain copy; a lone plain number/date (no natural series) becomes a
        // forced increment-by-1 series instead. Ctrl has no effect on formula fills.
        var forceCopyOnly = !sourceHasFormula && _ctrlHeld && (naturalScalarSeries is not null || naturalListSeries is not null);
        var scalarSeries = forceCopyOnly
            ? null
            : naturalScalarSeries ?? (!sourceHasFormula && _ctrlHeld ? TryCreateForcedSingleCellSeries(sheet, plan) : null);
        var listSeries = forceCopyOnly ? null : naturalListSeries;

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                var addr = new CellAddress(_sheetId, row, col);
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(row, col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                writtenCells.Add(addr);

                if (sourceCell is null)
                {
                    sheet.ClearCell(addr);
                    continue;
                }

                var offset = plan.Axis == FillAxis.Vertical
                    ? Math.Abs((int)addr.Row - (int)sourceAddr.Row)
                    : Math.Abs((int)addr.Col - (int)sourceAddr.Col);

                Cell newCell;
                if (scalarSeries is not null)
                {
                    newCell = Cell.FromValue(scalarSeries.CreateValue(scalarSeries.LastValue + scalarSeries.Step * offset));
                    newCell.StyleId = sourceCell.StyleId;
                }
                else if (listSeries is not null)
                {
                    newCell = Cell.FromValue(listSeries.ValueAt(offset));
                    newCell.StyleId = sourceCell.StyleId;
                }
                else
                {
                    // No detected trend/list series: replay the source range's own per-cell
                    // pattern cyclically instead of collapsing every destination cell to the
                    // single edge cell. A 2+ cell source (e.g. a running-total formula pair, or
                    // an alternating copy like "A","B") repeats its whole shape every
                    // sourceLength cells, matching Excel's fill-handle behavior.
                    var patternSourceAddr = ResolvePatternSourceAddress(plan, addr, sourceLength);
                    var patternSourceCell = sheet.GetCell(patternSourceAddr);
                    if (patternSourceCell is null)
                    {
                        sheet.ClearCell(addr);
                        continue;
                    }

                    if (!forceCopyOnly && patternSourceCell.HasFormula && patternSourceCell.FormulaText is not null)
                    {
                        int rowOffset = (int)addr.Row - (int)patternSourceAddr.Row;
                        int colOffset = (int)addr.Col - (int)patternSourceAddr.Col;
                        var shifted = FormulaRewriter.Rewrite(patternSourceCell.FormulaText,
                            new PasteOffsetOp(rowOffset, colOffset), sheet.Name)
                            ?? patternSourceCell.FormulaText;
                        newCell = Cell.FromFormula(shifted);
                    }
                    else
                    {
                        newCell = Cell.FromValue(patternSourceCell.Value);
                    }

                    newCell.StyleId = patternSourceCell.StyleId;
                }

                sheet.SetCell(addr, newCell);
            }
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    /// <summary>
    /// Excel semantics for dragging the fill handle inward: the portion of the original
    /// selection beyond the new (shrunk) boundary is cleared, exactly like Clear Contents.
    /// </summary>
    private CommandOutcome ApplyInwardClear(ICommandContext ctx, Sheet sheet)
    {
        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                var addr = new CellAddress(_sheetId, row, col);
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(row, col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                writtenCells.Add(addr);

                // Clear Contents semantics (like ClearContentsCommand): drop the value but keep
                // the cell's formatting in place, matching Excel's fill-handle-inward gesture.
                var cleared = Cell.FromValue(BlankValue.Instance);
                if (oldCell is not null)
                    cleared.StyleId = oldCell.StyleId;
                else if (oldStyleOnly.HasValue)
                    cleared.StyleId = oldStyleOnly.Value;
                sheet.SetCell(addr, cleared);
            }
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
        }
    }


    private bool TryGetFillPlan(out FillPlan plan)
    {
        plan = default;

        if (_sourceRange.Start.Sheet != _fillRange.Start.Sheet)
            return false;

        if (_sourceRange.Overlaps(_fillRange))
            return false;

        if (_sourceRange.ColCount == _fillRange.ColCount &&
            _sourceRange.Start.Col == _fillRange.Start.Col &&
            _sourceRange.End.Col == _fillRange.End.Col)
        {
            if (_fillRange.Start.Row == _sourceRange.End.Row + 1)
            {
                plan = new FillPlan(FillDirection.Down, FillAxis.Vertical);
                return true;
            }

            if (_sourceRange.Start.Row > 1 && _fillRange.End.Row + 1 == _sourceRange.Start.Row)
            {
                plan = new FillPlan(FillDirection.Up, FillAxis.Vertical);
                return true;
            }
        }

        if (_sourceRange.RowCount == _fillRange.RowCount &&
            _sourceRange.Start.Row == _fillRange.Start.Row &&
            _sourceRange.End.Row == _fillRange.End.Row)
        {
            if (_fillRange.Start.Col == _sourceRange.End.Col + 1)
            {
                plan = new FillPlan(FillDirection.Right, FillAxis.Horizontal);
                return true;
            }

            if (_sourceRange.Start.Col > 1 && _fillRange.End.Col + 1 == _sourceRange.Start.Col)
            {
                plan = new FillPlan(FillDirection.Left, FillAxis.Horizontal);
                return true;
            }
        }

        return false;
    }

    private CellAddress GetSourceEdgeAddress(FillPlan plan) => plan.Direction switch
    {
        FillDirection.Down => _sourceRange.End,
        FillDirection.Right => _sourceRange.End,
        FillDirection.Up => _sourceRange.Start,
        FillDirection.Left => _sourceRange.Start,
        _ => _sourceRange.End
    };

    /// <summary>
    /// Resolves which cell within <see cref="_sourceRange"/> a given destination cell should
    /// mirror when replaying the source's per-cell pattern (formula shape or plain copy) rather
    /// than a detected trend/list series. Excel repeats the whole source pattern cyclically every
    /// <paramref name="sourceLength"/> cells: the cell adjacent to the source mirrors the source
    /// cell nearest the fill edge, and each subsequent cell advances one step further into the
    /// pattern, wrapping back to the start of the pattern after <paramref name="sourceLength"/>
    /// cells.
    /// </summary>
    private CellAddress ResolvePatternSourceAddress(FillPlan plan, CellAddress addr, int sourceLength)
    {
        if (sourceLength <= 0)
            sourceLength = 1;

        switch (plan.Direction)
        {
            case FillDirection.Down:
            {
                var stepsAway = (int)addr.Row - (int)_sourceRange.End.Row - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, _sourceRange.Start.Row + (uint)patternIndex, addr.Col);
            }
            case FillDirection.Up:
            {
                var stepsAway = (int)_sourceRange.Start.Row - (int)addr.Row - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, _sourceRange.End.Row - (uint)patternIndex, addr.Col);
            }
            case FillDirection.Right:
            {
                var stepsAway = (int)addr.Col - (int)_sourceRange.End.Col - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, addr.Row, _sourceRange.Start.Col + (uint)patternIndex);
            }
            case FillDirection.Left:
            default:
            {
                var stepsAway = (int)_sourceRange.Start.Col - (int)addr.Col - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, addr.Row, _sourceRange.End.Col - (uint)patternIndex);
            }
        }
    }

    private int GetFillCellCapacity()
    {
        var count = _fillRange.CellCount;
        return count <= int.MaxValue ? (int)count : 0;
    }

    private ScalarSeries? TryCreateScalarSeries(Sheet sheet, FillPlan plan)
    {
        var isVertical = _sourceRange.ColCount == 1 && _sourceRange.RowCount >= 2;
        var isHorizontal = _sourceRange.RowCount == 1 && _sourceRange.ColCount >= 2;
        if (!isVertical && !isHorizontal)
            return null;

        var values = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .ToList();

        Func<double, ScalarValue>? createValue;
        if (values.All(value => value is NumberValue))
            createValue = serial => new NumberValue(serial);
        else if (values.All(value => value is DateTimeValue))
            createValue = serial => new DateTimeValue(serial);
        else
            return null;

        var numbers = values.Select(value => value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            _ => 0
        }).ToList();
        var lastValue = plan.Direction is FillDirection.Up or FillDirection.Left ? numbers[0] : numbers[^1];
        var naturalSlope = ComputeLinearFitSlope(numbers);
        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -naturalSlope : naturalSlope;

        return new ScalarSeries(lastValue, step, plan.Axis, createValue);
    }

    /// <summary>
    /// Ctrl-drag from a single plain number/date cell (no natural multi-cell series to detect)
    /// forces an incrementing series with a step of 1 day/unit, instead of the default copy.
    /// </summary>
    private ScalarSeries? TryCreateForcedSingleCellSeries(Sheet sheet, FillPlan plan)
    {
        if (_sourceRange.CellCount != 1)
            return null;

        var value = sheet.GetCell(_sourceRange.Start)?.Value;
        Func<double, ScalarValue> createValue;
        double seed;
        switch (value)
        {
            case NumberValue number:
                createValue = serial => new NumberValue(serial);
                seed = number.Value;
                break;
            case DateTimeValue date:
                createValue = serial => new DateTimeValue(serial);
                seed = date.Value;
                break;
            default:
                return null;
        }

        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1;
        return new ScalarSeries(seed, step, plan.Axis, createValue);
    }

    /// <summary>
    /// Detects the two non-numeric Excel fill-handle series: text ending in a number
    /// (e.g. "Item 1", "Item 2" -&gt; "Item 3") and membership in one of Excel's built-in
    /// auto-fill lists (weekday/month names, full or abbreviated), which wrap around after
    /// the last entry. Requires at least one source cell and, for text-with-number, either a
    /// single source cell (auto-increments by 1) or a source range whose trailing numbers all
    /// share the same prefix/suffix and advance by a constant step.
    /// </summary>
    private ListSeries? TryCreateListSeries(Sheet sheet, FillPlan plan)
    {
        var isVertical = _sourceRange.ColCount == 1 && _sourceRange.RowCount >= 1;
        var isHorizontal = _sourceRange.RowCount == 1 && _sourceRange.ColCount >= 1;
        if (!isVertical && !isHorizontal)
            return null;

        var texts = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .Select(value => value is TextValue text ? text.Value : null)
            .ToList();
        if (texts.Any(text => text is null))
            return null;
        var values = texts.Cast<string>().ToList();

        return TryCreateTrailingNumberSeries(values, plan)
            ?? TryCreateBuiltInListSeries(values, plan);
    }

    /// <summary>Text ending in a run of digits (optionally with leading zeros): "Item 1" -&gt; "Item 2", ...</summary>
    private static ListSeries? TryCreateTrailingNumberSeries(IReadOnlyList<string> values, FillPlan plan)
    {
        var parsed = values.Select(TrySplitTrailingNumber).ToList();
        if (parsed.Any(part => part is null))
            return null;

        var prefix = parsed[0]!.Value.Prefix;
        var width = parsed[0]!.Value.Width;
        if (parsed.Any(part => part!.Value.Prefix != prefix))
            return null;

        var numbers = parsed.Select(part => (double)part!.Value.Number).ToList();
        double step = numbers.Count >= 2
            ? ComputeLinearFitSlope(numbers)
            : (plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1);
        var lastNumber = plan.Direction is FillDirection.Up or FillDirection.Left ? numbers[0] : numbers[^1];
        var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;

        return new ListSeries(plan.Axis, offset =>
        {
            var next = (long)Math.Round(lastNumber + directedStep * offset);
            var digits = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (next >= 0 && digits.Length < width)
                digits = digits.PadLeft(width, '0');
            return new TextValue(prefix + digits);
        });
    }

    private static (string Prefix, int Width, long Number)? TrySplitTrailingNumber(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsAsciiDigit(text[i - 1]))
            i--;
        if (i == text.Length)
            return null; // no trailing digits at all

        var digits = text[i..];
        if (!long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return null; // too large / not a plain digit run

        return (text[..i], digits.Length, number);
    }

    private static readonly string[][] BuiltInLists =
    [
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    ];

    /// <summary>Excel's built-in weekday/month name lists, wrapping around after the last entry.</summary>
    private static ListSeries? TryCreateBuiltInListSeries(IReadOnlyList<string> values, FillPlan plan)
    {
        foreach (var list in BuiltInLists)
        {
            var indices = values
                .Select(value => Array.FindIndex(list, entry => string.Equals(entry, value, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (indices.Any(index => index < 0))
                continue;

            var step = indices.Count >= 2
                ? (int)Math.Round(ComputeLinearFitSlope(indices.Select(i => (double)i).ToList()))
                : (plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1);
            var lastIndex = plan.Direction is FillDirection.Up or FillDirection.Left ? indices[0] : indices[^1];
            var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;
            if (directedStep == 0)
                directedStep = 1;

            return new ListSeries(plan.Axis, offset =>
            {
                var index = Mod(lastIndex + directedStep * (int)offset, list.Length);
                return new TextValue(list[index]);
            });
        }

        return null;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>
    /// Fits a straight line (least-squares) through <paramref name="numbers"/> (treated as
    /// y-values at evenly spaced x = 0, 1, 2, ...) and returns its slope, matching Excel's
    /// fill-handle behavior for a linear numeric/date trend. For exactly two values this
    /// reduces to the plain two-point slope (numbers[1] - numbers[0]).
    /// </summary>
    private static double ComputeLinearFitSlope(IReadOnlyList<double> numbers)
    {
        var n = numbers.Count;
        if (n < 2)
            return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += numbers[i];
            sumXY += i * numbers[i];
            sumXX += (double)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0)
            return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }

    private sealed record ScalarSeries(
        double LastValue,
        double Step,
        FillAxis Axis,
        Func<double, ScalarValue> CreateValue);

    private sealed record ListSeries(FillAxis Axis, Func<int, ScalarValue> ValueAt);

    private readonly record struct FillPlan(FillDirection Direction, FillAxis Axis);

    private enum FillDirection
    {
        Down,
        Right,
        Up,
        Left
    }

    private enum FillAxis
    {
        Vertical,
        Horizontal
    }

}
