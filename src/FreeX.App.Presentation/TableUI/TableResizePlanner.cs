using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>
/// The validated outcome of the Resize Table dialog: the resolved new data range and the (trimmed)
/// reference text the user entered.
/// </summary>
public sealed record TableResizeChange(GridRange NewRange, string NewRangeText);

/// <summary>
/// Portable, UI-free planning for the structured-table "Resize Table" dialog: capturing the table's current
/// range as the dialog's initial reference text, and validating/resolving a typed range reference into the
/// <see cref="GridRange"/> the resize command needs. The resolved range must stay on the table's sheet, keep
/// the table's existing top-left header cell fixed, and span at least two rows (header + one data row) and one
/// column — mirroring the Core <c>ResizeStructuredTableCommand</c> guard so the dialog reports a clear message
/// up front. Reference resolution is supplied by the host as a delegate (the shells pass
/// <c>WorkbookSession.TryResolveReferenceRange</c>) so the planner stays free of workbook/UI types and is
/// unit-testable. Building the dialog and running the command (the host hands
/// <see cref="TableResizeChange.NewRange"/> to <c>ResizeStructuredTableCommand</c>) stays with each shell.
/// </summary>
public static class TableResizePlanner
{
    public const string EmptyReferenceMessage =
        "Enter the range for the resized table.";

    public const string InvalidReferenceMessage =
        "Enter a valid range reference for the resized table.";

    public const string DifferentSheetMessage =
        "The resized table range must stay on the table's sheet.";

    public const string MovedHeaderMessage =
        "Resize Table keeps the current table's top-left cell fixed.";

    public const string TooFewRowsMessage =
        "The resized table range needs a header row and at least one row of data.";

    public const string NoColumnsMessage =
        "The resized table range needs at least one column.";

    /// <summary>Resolves an A1 / named-range / table reference to a <see cref="GridRange"/> (false when invalid).</summary>
    public delegate bool ReferenceResolver(string reference, out GridRange range);

    /// <summary>
    /// Formats a <see cref="GridRange"/> as the A1 reference text the dialog seeds its input box with
    /// (column letters + 1-based row, collapsing a single cell).
    /// </summary>
    public static string FormatRange(GridRange range)
    {
        var start = FormatCell(range.Start);
        var end = FormatCell(range.End);
        return string.Equals(start, end, StringComparison.Ordinal) ? start : $"{start}:{end}";
    }

    /// <summary>Snapshots the table's current range as the dialog's initial reference text.</summary>
    public static string Capture(StructuredTableModel table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return FormatRange(table.Range);
    }

    /// <summary>
    /// Validates the dialog's reference text and resolves it through <paramref name="resolve"/> against the
    /// table being resized. On success returns the change with the resolved range and trimmed text; on
    /// failure reports a user-facing message.
    /// </summary>
    public static bool TryCreateResize(
        StructuredTableModel table,
        string? referenceText,
        ReferenceResolver resolve,
        out TableResizeChange? change,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(resolve);
        change = null;
        error = null;

        var trimmed = referenceText?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = EmptyReferenceMessage;
            return false;
        }

        if (!resolve(trimmed, out var range))
        {
            error = InvalidReferenceMessage;
            return false;
        }

        if (range.Start.Sheet != table.Range.Start.Sheet || range.End.Sheet != table.Range.End.Sheet)
        {
            error = DifferentSheetMessage;
            return false;
        }

        if (range.Start != table.Range.Start)
        {
            error = MovedHeaderMessage;
            return false;
        }

        if (range.RowCount < 2)
        {
            error = TooFewRowsMessage;
            return false;
        }

        if (range.ColCount == 0)
        {
            error = NoColumnsMessage;
            return false;
        }

        change = new TableResizeChange(range, trimmed);
        return true;
    }

    private static string FormatCell(CellAddress address) =>
        CellAddress.NumberToColumnName(address.Col) +
        address.Row.ToString(CultureInfo.InvariantCulture);
}
