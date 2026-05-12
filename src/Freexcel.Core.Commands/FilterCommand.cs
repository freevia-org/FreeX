using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Applies or clears a value filter on a range by toggling Sheet.HiddenRows.
/// Rows whose filter-column value is not in <c>allowedValues</c> are hidden.
/// Passing an empty/null <c>allowedValues</c> clears all hidden rows.
/// </summary>
public sealed class FilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;   // 0 = first column of the range
    private readonly IReadOnlyList<string> _allowedValues;

    // Snapshot of previously hidden rows for undo
    private HashSet<uint>? _previousHiddenRows;

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

        // Snapshot existing hidden-row state for undo
        _previousHiddenRows = [.. sheet.HiddenRows];

        // Clear existing filter state
        sheet.HiddenRows.Clear();

        if (_allowedValues.Count == 0)
        {
            // No allowed values = clear filter — all rows visible
            return new CommandOutcome(true);
        }

        uint filterCol  = _range.Start.Col + _filterColOffset;
        uint startRow   = _range.Start.Row;
        uint endRow     = _range.End.Row;

        // Build a case-insensitive lookup of allowed values
        var allowed = new HashSet<string>(_allowedValues, StringComparer.OrdinalIgnoreCase);

        // Skip the first row of the range — treat it as a header row
        for (uint row = startRow + 1; row <= endRow; row++)
        {
            var value = sheet.GetValue(row, filterCol);
            var text  = ScalarToString(value);

            if (!allowed.Contains(text))
                sheet.HiddenRows.Add(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.HiddenRows.Clear();
        foreach (var r in _previousHiddenRows)
            sheet.HiddenRows.Add(r);
    }

    private static string ScalarToString(ScalarValue value) => value switch
    {
        TextValue t      => t.Value,
        NumberValue n    => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b      => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd"),
        BlankValue       => "",
        ErrorValue e     => e.Code,
        _                => ""
    };
}
