using FreeX.Core.Model;
using System.Globalization;
using System.Text;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies or clears a value filter on a range by toggling Sheet.FilterHiddenRows.
/// Rows whose filter-column value is not in <c>allowedValues</c> are hidden.
/// Passing an empty/null <c>allowedValues</c> clears all hidden rows.
/// </summary>
public sealed class FilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;   // 0 = first column of the range
    private readonly IReadOnlyList<string> _allowedValues;

    private FilterUndoSnapshot _undoSnapshot;

    public string Label => _allowedValues.Count == 0 ? "Clear Filter" : "Apply Filter";

    public FilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        IReadOnlyList<string> allowedValues)
    {
        _sheetId = sheetId;
        _range   = range;
        _filterColOffset = filterColOffset;
        _allowedValues   = allowedValues ?? [];
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet    = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Reset();

        uint filterCol  = _range.Start.Col + _filterColOffset;
        uint startRow   = _range.Start.Row;
        uint endRow     = _range.End.Row;

        if (_allowedValues.Count == 0)
        {
            if (!FilterHiddenRowUpdater.ContainsAnyInRange(sheet.FilterHiddenRows, _range))
                return new CommandOutcome(true);

            _undoSnapshot.CaptureIfNeeded(sheet);
            FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _range);
            return new CommandOutcome(true);
        }

        var allowed = FilterAllowedValueMatcher.Create(_allowedValues);

        for (uint row = startRow + 1; row <= endRow; row++)
        {
            var value = sheet.GetValue(row, filterCol);
            var text  = FilterValueFormatter.ToText(value);
            var shouldHide = !allowed.Contains(text);
            if (sheet.FilterHiddenRows.Contains(row) == shouldHide)
                continue;

            _undoSnapshot.CaptureIfNeeded(sheet);
            FilterHiddenRowUpdater.SetHidden(sheet.FilterHiddenRows, row, shouldHide);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot) return;
        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}

public sealed class CellFillColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly CellColor _fillColor;
    private FilterUndoSnapshot _undoSnapshot;

    public string Label => "Filter by Cell Color";

    public CellFillColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        CellColor fillColor)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _fillColor = fillColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var styleId = sheet.GetCell(row, filterCol)?.StyleId ??
                sheet.GetStyleOnly(row, filterCol) ??
                StyleId.Default;
            var fillColor = ctx.Workbook.GetStyle(styleId).FillColor;
            FilterHiddenRowUpdater.SetVisible(sheet.FilterHiddenRows, row, fillColor == _fillColor);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}

public sealed class CellNoFillColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private FilterUndoSnapshot _undoSnapshot;

    public string Label => "Filter by No Fill";

    public CellNoFillColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var styleId = sheet.GetCell(row, filterCol)?.StyleId ??
                sheet.GetStyleOnly(row, filterCol) ??
                StyleId.Default;
            var fillColor = ctx.Workbook.GetStyle(styleId).FillColor;
            FilterHiddenRowUpdater.SetVisible(sheet.FilterHiddenRows, row, fillColor is null);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}

public sealed class CellFontColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly CellColor _fontColor;
    private FilterUndoSnapshot _undoSnapshot;

    public string Label => "Filter by Font Color";

    public CellFontColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        CellColor fontColor)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _fontColor = fontColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var styleId = sheet.GetCell(row, filterCol)?.StyleId ??
                sheet.GetStyleOnly(row, filterCol) ??
                StyleId.Default;
            var fontColor = ctx.Workbook.GetStyle(styleId).FontColor;
            FilterHiddenRowUpdater.SetVisible(sheet.FilterHiddenRows, row, fontColor == _fontColor);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}

internal struct FilterUndoSnapshot
{
    private uint[]? _hiddenRows;
    private uint[]? _filterHiddenRows;

    public bool HasSnapshot => _hiddenRows is not null;

    public void Reset()
    {
        _hiddenRows = null;
        _filterHiddenRows = null;
    }

    public void Capture(Sheet sheet)
    {
        _hiddenRows = [.. sheet.HiddenRows];
        _filterHiddenRows = [.. sheet.FilterHiddenRows];
    }

    public void CaptureIfNeeded(Sheet sheet)
    {
        if (HasSnapshot)
            return;

        Capture(sheet);
    }

    public void Restore(Sheet sheet)
    {
        if (_hiddenRows is null)
            return;

        sheet.HiddenRows.Clear();
        sheet.HiddenRows.UnionWith(_hiddenRows);
        sheet.FilterHiddenRows.Clear();
        if (_filterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_filterHiddenRows);
    }
}

internal readonly struct FilterAllowedValueMatcher
{
    private readonly string? _singleValue;
    private readonly HashSet<string>? _values;

    private FilterAllowedValueMatcher(string singleValue)
    {
        _singleValue = singleValue;
        _values = null;
    }

    private FilterAllowedValueMatcher(HashSet<string> values)
    {
        _singleValue = null;
        _values = values;
    }

    public static FilterAllowedValueMatcher Create(IReadOnlyList<string> values) =>
        values.Count == 1
            ? new FilterAllowedValueMatcher(values[0])
            : new FilterAllowedValueMatcher(new HashSet<string>(values, StringComparer.OrdinalIgnoreCase));

    public bool Contains(string text) =>
        _values is not null
            ? _values.Contains(text)
            : string.Equals(text, _singleValue, StringComparison.OrdinalIgnoreCase);
}

internal static class FilterHiddenRowUpdater
{
    public static void SetHidden(HashSet<uint> filterHiddenRows, uint row, bool hidden)
    {
        if (hidden)
            filterHiddenRows.Add(row);
        else
            filterHiddenRows.Remove(row);
    }

    public static void SetVisible(HashSet<uint> filterHiddenRows, uint row, bool visible)
    {
        SetHidden(filterHiddenRows, row, !visible);
    }

    public static void ClearRange(HashSet<uint> filterHiddenRows, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return;

        var dataRowCount = lastDataRow - firstDataRow + 1;
        if ((uint)filterHiddenRows.Count < dataRowCount)
        {
            filterHiddenRows.RemoveWhere(row => row >= firstDataRow && row <= lastDataRow);
            return;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
            filterHiddenRows.Remove(row);
    }

    public static bool ContainsAnyInRange(HashSet<uint> filterHiddenRows, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return false;

        var dataRowCount = lastDataRow - firstDataRow + 1;
        if ((uint)filterHiddenRows.Count < dataRowCount)
        {
            foreach (var row in filterHiddenRows)
            {
                if (row >= firstDataRow && row <= lastDataRow)
                    return true;
            }

            return false;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (filterHiddenRows.Contains(row))
                return true;
        }

        return false;
    }
}

internal static class FilterValueFormatter
{
    public static string ToText(ScalarValue value) => value switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd"),
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => ""
    };

    public static void AppendText(StringBuilder builder, ScalarValue value)
    {
        switch (value)
        {
            case TextValue text:
                builder.Append(text.Value);
                break;
            case NumberValue number:
                AppendInvariant(builder, number.Value);
                break;
            case BoolValue boolean:
                builder.Append(boolean.Value ? "TRUE" : "FALSE");
                break;
            case DateTimeValue dateTime:
                AppendDate(builder, dateTime);
                break;
            case ErrorValue error:
                builder.Append(error.Code);
                break;
        }
    }

    private static void AppendInvariant(StringBuilder builder, double value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var charsWritten, provider: CultureInfo.InvariantCulture))
            builder.Append(buffer[..charsWritten]);
        else
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendDate(StringBuilder builder, DateTimeValue value)
    {
        Span<char> buffer = stackalloc char[10];
        var date = value.ToDateTime();
        if (date.TryFormat(buffer, out var charsWritten, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            builder.Append(buffer[..charsWritten]);
        else
            builder.Append(date.ToString("yyyy-MM-dd"));
    }
}
