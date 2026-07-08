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
    // H18: when _range is a structured table's range, table.FilterColumns (the model
    // XlsxStructuredTableWriter actually serializes into the table's <autoFilter>/<filterColumn> XML)
    // must be kept in sync with the interactive filter, otherwise the filter is visibly applied but
    // silently lost the moment the workbook is saved and reopened. -1 = no table matched this range.
    private int _tableId = -1;
    private List<StructuredTableFilterColumnModel>? _previousTableFilterColumns;

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
        _undoSnapshot.CaptureIfNeeded(sheet);

        uint filterCol  = _range.Start.Col + _filterColOffset;

        // F8: a plain flat FilterHiddenRows set cannot represent "AND across columns" — hiding a row
        // for column A and then evaluating column B in isolation would un-hide rows that column A
        // hid but column B doesn't care about. Excel hides a row if it fails ANY active column's
        // filter (hidden set = union of every column's exclusions), so we track each column's
        // allowed-values set separately in sheet.ActiveValueFilterColumns and always recompute
        // FilterHiddenRows over the range from that full set of active columns, rather than mutating
        // the flat set from a single column's perspective.
        if (_allowedValues.Count == 0)
            sheet.ActiveValueFilterColumns.Remove(filterCol);
        else
            sheet.ActiveValueFilterColumns[filterCol] = _allowedValues;

        RecomputeHiddenRows(sheet, _range);

        ApplyToStructuredTableIfMatched(sheet);

        return new CommandOutcome(true);
    }

    /// <summary>
    /// If <see cref="_range"/> is exactly a structured table's range (the shape
    /// AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange hands back for a table's header-cell
    /// filter dropdown), mirror the applied/cleared filter into that table's FilterColumns model so
    /// it round-trips through XlsxStructuredTableWriter instead of being silently dropped on save.
    /// </summary>
    private void ApplyToStructuredTableIfMatched(Sheet sheet)
    {
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            var table = sheet.StructuredTables[i];
            if (!table.Range.Equals(_range))
                continue;

            _tableId = table.Id;
            _previousTableFilterColumns = [.. table.FilterColumns];

            var filterColumns = table.FilterColumns
                .Where(fc => fc.ColumnId != (int)_filterColOffset)
                .ToList();
            if (_allowedValues.Count > 0)
                filterColumns.Add(new StructuredTableFilterColumnModel((int)_filterColOffset, _allowedValues));
            filterColumns.Sort(static (a, b) => a.ColumnId.CompareTo(b.ColumnId));

            sheet.StructuredTables[i] = StructuredTableDesignCommandHelpers.CopyTable(table, filterColumns: filterColumns);
            return;
        }
    }

    private static void RecomputeHiddenRows(Sheet sheet, GridRange range)
    {
        uint startRow = range.Start.Row;
        uint endRow   = range.End.Row;

        // G7: Top10/Average/color/custom-criterion filters hide rows by mutating FilterHiddenRows
        // directly, without registering anything in ActiveValueFilterColumns. This recompute must
        // only ever decide the hidden state of rows it "owns" (sheet.ValueFilterHiddenRows, the
        // rows this very mechanism hid last time it ran) — any other row currently hidden was put
        // there by one of those other mechanisms and must survive this recompute untouched.
        if (sheet.ActiveValueFilterColumns.Count == 0)
        {
            FilterHiddenRowUpdater.ClearOwnedRows(sheet, range, sheet.ValueFilterHiddenRows);
            sheet.ValueFilterHiddenRows.Clear();
            return;
        }

        // Pre-build a matcher per active column so we don't rebuild one per row.
        var matchers = new (uint Col, FilterAllowedValueMatcher Matcher)[sheet.ActiveValueFilterColumns.Count];
        var i = 0;
        foreach (var (col, allowedValues) in sheet.ActiveValueFilterColumns)
        {
            matchers[i++] = (col, FilterAllowedValueMatcher.Create(allowedValues));
        }

        // Rows this mechanism owned BEFORE this recompute — only these may be un-hidden below.
        var previouslyOwnedRows = sheet.ValueFilterHiddenRows.Count == 0
            ? null
            : new HashSet<uint>(sheet.ValueFilterHiddenRows);
        sheet.ValueFilterHiddenRows.Clear();

        for (uint row = startRow + 1; row <= endRow; row++)
        {
            var shouldHide = false;
            foreach (var (col, matcher) in matchers)
            {
                var value = sheet.GetValue(row, col);
                var text  = FilterValueFormatter.ToText(value);
                if (!matcher.Contains(text))
                {
                    shouldHide = true;
                    break;
                }
            }

            if (shouldHide)
            {
                sheet.ValueFilterHiddenRows.Add(row);
                sheet.FilterHiddenRows.Add(row);
            }
            else if (previouslyOwnedRows is not null && previouslyOwnedRows.Contains(row))
            {
                // Only relinquish rows THIS mechanism previously hid, and only when no OTHER
                // active mechanism (a condition/average/top-bottom/color filter on ANY column,
                // tracked in sheet.ColumnFilterOwnedRows) still needs the row hidden. Mirrors
                // ApplyColumnOwnedVisibility's symmetric ownership check on the condition side
                // (finding R13-meta-3) — without it, clearing/loosening a value filter could
                // un-hide a row a still-active condition/color/Top-Bottom filter on another
                // column is responsible for hiding, breaking Excel's AND-across-columns semantics.
                if (!FilterHiddenRowUpdater.IsHiddenByAnyColumnOwnedFilter(sheet, row))
                    sheet.FilterHiddenRows.Remove(row);
            }
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot) return;
        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);

        if (_tableId != -1 && _previousTableFilterColumns is not null &&
            CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
        {
            var table = sheet.StructuredTables[tableIndex];
            sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
                table, filterColumns: _previousTableFilterColumns);
        }
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
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fillColor == _fillColor);
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
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fillColor is null);
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
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fontColor == _fontColor);
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
    // F8: per-column value-filter state (sheet.ActiveValueFilterColumns) must roll back alongside
    // FilterHiddenRows, otherwise undoing a FilterCommand would leave a stale column entry behind
    // that corrupts the next recompute's AND-across-columns union.
    private Dictionary<uint, IReadOnlyList<string>>? _activeValueFilterColumns;
    // G7: sheet.ValueFilterHiddenRows (which rows the value-filter mechanism itself currently owns)
    // must roll back in lockstep with ActiveValueFilterColumns/FilterHiddenRows too, otherwise an
    // undo could leave it out of sync with the restored FilterHiddenRows and corrupt the next
    // recompute's "preserve rows I don't own" logic.
    private uint[]? _valueFilterHiddenRows;
    // R12-sort-filter-1: sheet.ColumnFilterOwnedRows (which rows each condition/average/top-bottom/
    // color column filter currently owns) must roll back alongside FilterHiddenRows too, otherwise
    // an undo could leave a stale/mismatched ownership entry behind that corrupts the next
    // ApplyColumnOwnedVisibility/ClearColumnOwnedRange AND-across-columns decision.
    private Dictionary<uint, HashSet<uint>>? _columnFilterOwnedRows;

    public bool HasSnapshot => _hiddenRows is not null;

    public void Reset()
    {
        _hiddenRows = null;
        _filterHiddenRows = null;
        _activeValueFilterColumns = null;
        _valueFilterHiddenRows = null;
        _columnFilterOwnedRows = null;
    }

    public void Capture(Sheet sheet)
    {
        _hiddenRows = [.. sheet.HiddenRows];
        _filterHiddenRows = [.. sheet.FilterHiddenRows];
        _activeValueFilterColumns = sheet.ActiveValueFilterColumns.Count == 0
            ? null
            : sheet.ActiveValueFilterColumns.ToDictionary(
                kvp => kvp.Key,
                IReadOnlyList<string> (kvp) => [.. kvp.Value]);
        _valueFilterHiddenRows = [.. sheet.ValueFilterHiddenRows];
        _columnFilterOwnedRows = sheet.ColumnFilterOwnedRows.Count == 0
            ? null
            : sheet.ColumnFilterOwnedRows.ToDictionary(
                kvp => kvp.Key,
                HashSet<uint> (kvp) => [.. kvp.Value]);
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

        sheet.ActiveValueFilterColumns.Clear();
        if (_activeValueFilterColumns is not null)
        {
            foreach (var (col, allowedValues) in _activeValueFilterColumns)
                sheet.ActiveValueFilterColumns[col] = allowedValues;
        }

        sheet.ValueFilterHiddenRows.Clear();
        if (_valueFilterHiddenRows is not null)
            sheet.ValueFilterHiddenRows.UnionWith(_valueFilterHiddenRows);

        sheet.ColumnFilterOwnedRows.Clear();
        if (_columnFilterOwnedRows is not null)
        {
            foreach (var (col, ownedRows) in _columnFilterOwnedRows)
                sheet.ColumnFilterOwnedRows[col] = [.. ownedRows];
        }
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

    /// <summary>
    /// Like <see cref="ClearRange"/>, but only un-hides rows in <paramref name="ownedRows"/> — rows
    /// hidden for some other reason (e.g. a Top10/Average/color/custom-criterion filter on another
    /// column) are left hidden. Used when the value-filter mechanism has no active columns left and
    /// must relinquish only the rows it previously owned (see finding G7). Also consults
    /// <see cref="Sheet.ColumnFilterOwnedRows"/> (finding R13-meta-3) so a row still owned by an
    /// active condition/average/top-bottom/color filter on any column is never un-hidden here.
    /// </summary>
    public static void ClearOwnedRows(Sheet sheet, GridRange range, IReadOnlyCollection<uint> ownedRows)
    {
        if (ownedRows.Count == 0)
            return;

        var filterHiddenRows = sheet.FilterHiddenRows;
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return;

        foreach (var row in ownedRows)
        {
            if (row >= firstDataRow && row <= lastDataRow && !IsHiddenByAnyColumnOwnedFilter(sheet, row))
                filterHiddenRows.Remove(row);
        }
    }

    /// <summary>
    /// Applies a single row's visible/hidden decision for the non-value-list AutoFilter mechanisms
    /// (condition/custom-criterion, Top 10/Above-Average, and cell/font-color filters), recording
    /// ownership per <paramref name="filterCol"/> in <see cref="Sheet.ColumnFilterOwnedRows"/> so a
    /// later re-evaluation of THIS SAME column never un-hides a row that some OTHER active column's
    /// filter (a value-list filter via <see cref="Sheet.ActiveValueFilterColumns"/>, or another
    /// condition/average/top-bottom/color filter) is responsible for hiding (finding
    /// R12-sort-filter-1). Excel ANDs AutoFilter criteria across columns — a row is hidden if it
    /// fails ANY active column's filter — so un-hiding must only ever relinquish rows this column's
    /// own mechanism previously claimed.
    /// </summary>
    public static void ApplyColumnOwnedVisibility(Sheet sheet, uint filterCol, uint row, bool visible)
    {
        if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned))
        {
            owned = [];
            sheet.ColumnFilterOwnedRows[filterCol] = owned;
        }

        if (visible)
        {
            // Only relinquish the row from FilterHiddenRows if no OTHER active mechanism (a
            // value-list filter on any column, or another condition/average/top-bottom/color
            // filter's owned rows) is also responsible for hiding it.
            owned.Remove(row);
            if (!IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                sheet.FilterHiddenRows.Remove(row);
        }
        else
        {
            owned.Add(row);
            sheet.FilterHiddenRows.Add(row);
        }
    }

    private static bool IsHiddenByAnyOtherActiveMechanism(Sheet sheet, uint excludeCol, uint row)
    {
        if (sheet.ValueFilterHiddenRows.Contains(row))
            return true;

        foreach (var (col, owned) in sheet.ColumnFilterOwnedRows)
        {
            if (col != excludeCol && owned.Contains(row))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when any condition/average/top-bottom/color filter, on ANY column, currently owns
    /// (and therefore is responsible for hiding) <paramref name="row"/>. This is the value-filter
    /// side's counterpart to <see cref="IsHiddenByAnyOtherActiveMechanism"/> — the value-filter
    /// recompute/clear paths have no single "owning column" of their own (a value filter can span
    /// several columns via <see cref="Sheet.ActiveValueFilterColumns"/>), so there is no column to
    /// exclude: any owned row must survive relinquishment (finding R13-meta-3).
    /// </summary>
    public static bool IsHiddenByAnyColumnOwnedFilter(Sheet sheet, uint row)
    {
        foreach (var owned in sheet.ColumnFilterOwnedRows.Values)
        {
            if (owned.Contains(row))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cheap short-circuit for <see cref="ApplyColumnOwnedVisibility"/>'s callers: true when calling
    /// it for <paramref name="row"/>/<paramref name="visible"/> would be a strict no-op (no ownership
    /// change, no FilterHiddenRows change) — i.e. the row is already NOT owned by this column and
    /// already not hidden (when <paramref name="visible"/> is true), or already owned by this column
    /// (and therefore already hidden) when <paramref name="visible"/> is false. Skipping the call in
    /// that case avoids per-row HashSet churn on dense filters without risking the AND-across-columns
    /// bug this ownership tracking exists to fix (finding R12-sort-filter-1) — an un-owned but
    /// otherwise-hidden row (e.g. hidden directly, or by another column) is intentionally NOT treated
    /// as a no-op so it still gets a chance to be relinquished when this column now wants it visible.
    /// </summary>
    public static bool IsColumnOwnedVisibilityAlreadyCorrect(Sheet sheet, uint filterCol, uint row, bool visible)
    {
        var owned = sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var ownedRows) && ownedRows.Contains(row);
        return visible
            ? !owned && !sheet.FilterHiddenRows.Contains(row)
            : owned;
    }

    /// <summary>
    /// Relinquishes every row in <paramref name="range"/> that <paramref name="filterCol"/>'s own
    /// condition/average/top-bottom/color filter currently owns (e.g. when that filter is being
    /// cleared, or degenerately matches nothing/everything), without disturbing rows some OTHER
    /// active column's filter is hiding. Mirrors <see cref="ApplyColumnOwnedVisibility"/>'s
    /// ownership discipline for the bulk-clear code paths (finding R12-sort-filter-1).
    /// </summary>
    public static void ClearColumnOwnedRange(Sheet sheet, uint filterCol, GridRange range)
    {
        if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned) || owned.Count == 0)
            return;

        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        foreach (var row in owned)
        {
            if (row < firstDataRow || row > lastDataRow)
                continue;
            if (!IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                sheet.FilterHiddenRows.Remove(row);
        }

        owned.Clear();
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

/// <summary>
/// Maps a <see cref="ScalarValue"/> to the canonical text a value filter matches against: text as-is,
/// numbers in <see cref="CultureInfo.InvariantCulture"/>, bools <c>TRUE</c>/<c>FALSE</c>, dates
/// <c>yyyy-MM-dd</c>, blanks empty, errors as their code. This is the single source of truth for the
/// filter value text — both the desktop and Avalonia dropdown checklists format cell values with
/// <see cref="ToText"/> so the values they show agree exactly with what <see cref="FilterCommand"/>
/// matches.
/// </summary>
public static class FilterValueFormatter
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
