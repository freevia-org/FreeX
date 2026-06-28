using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// The validated outcome of the Change PivotTable Data Source dialog: the resolved source
/// <see cref="GridRange"/> and the (trimmed) reference text the user entered.
/// </summary>
public sealed record PivotDataSourceChange(GridRange SourceRange, string SourceRangeText);

/// <summary>
/// Portable, UI-free planning for the "Change PivotTable Data Source" dialog: capturing the pivot's current
/// source range as the dialog's initial reference text, and validating/resolving a typed range or table
/// reference into the <see cref="GridRange"/> the change command needs (it must be a real range with a header
/// row and at least one data row, matching the Core <c>ChangePivotTableSourceCommand</c> guard). Reference
/// resolution is supplied by the host as a delegate (the shells pass
/// <c>WorkbookSession.TryResolveReferenceRange</c>) so the planner stays free of workbook/UI types and is
/// unit-testable. Building the dialog and running the command stays with each shell's command glue (the host
/// hands <see cref="PivotDataSourceChange.SourceRange"/> to <c>ChangePivotTableSourceCommand</c>).
/// </summary>
public static class PivotDataSourcePlanner
{
    public const string EmptyReferenceMessage =
        "Enter the range or table that contains the PivotTable's source data.";

    public const string InvalidReferenceMessage =
        "Enter a valid range or table reference for the PivotTable source data.";

    public const string MissingHeadersMessage =
        "The PivotTable source range needs a header row and at least one row of data.";

    /// <summary>Resolves an A1 / named-range / table reference to a <see cref="GridRange"/> (false when invalid).</summary>
    public delegate bool ReferenceResolver(string reference, out GridRange range);

    /// <summary>
    /// Formats a <see cref="GridRange"/> as the A1 reference text the dialog seeds its input box with. Mirrors
    /// the shells' own range-reference formatting (column letters + 1-based row, collapsing a single cell).
    /// </summary>
    public static string FormatSourceRange(GridRange range)
    {
        var start = FormatCell(range.Start);
        var end = FormatCell(range.End);
        return string.Equals(start, end, StringComparison.Ordinal) ? start : $"{start}:{end}";
    }

    /// <summary>Snapshots the pivot's current source range as the dialog's initial reference text.</summary>
    public static string Capture(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return FormatSourceRange(pivotTable.SourceRange);
    }

    /// <summary>Normalizes reference text collected from a dialog or range-selection request.</summary>
    public static string NormalizeReferenceText(string? referenceText) =>
        referenceText?.Trim() ?? string.Empty;

    /// <summary>
    /// Validates the dialog's reference text and resolves it through <paramref name="resolve"/>. On success
    /// returns the change with the resolved range and trimmed text; on failure reports a user-facing message.
    /// </summary>
    public static bool TryCreateChange(
        string? referenceText,
        ReferenceResolver resolve,
        out PivotDataSourceChange? change,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        change = null;
        error = null;

        var trimmed = NormalizeReferenceText(referenceText);
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

        // Mirror the Core change-source guard: a PivotTable source needs a header row plus at least one
        // data row (so >= 2 rows) and at least one column. Reject up front for a clear dialog message.
        if (range.ColCount == 0 || range.RowCount < 2)
        {
            error = MissingHeadersMessage;
            return false;
        }

        change = new PivotDataSourceChange(range, trimmed);
        return true;
    }

    private static string FormatCell(CellAddress address) =>
        CellAddress.NumberToColumnName(address.Col) +
        address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
