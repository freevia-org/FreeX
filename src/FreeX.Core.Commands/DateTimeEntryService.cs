using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class DateTimeEntryService
{
    /// <summary>
    /// Excel's built-in short-date format (numFmtId 14). This invariant OOXML format code is
    /// intentionally persisted instead of a localized Windows display pattern.
    /// </summary>
    public const string CurrentDateNumberFormat = "m/d/yy";

    public static DateTimeValue CurrentDate(DateTime now) =>
        DateTimeValue.FromDateTime(now.Date);

    /// <summary>
    /// Returns the numeric Excel serial Ctrl+; writes. The grid and formula bar derive their
    /// localized text from the cell's built-in short-date number format.
    /// </summary>
    public static NumberValue CurrentDateSerial(DateTime now) =>
        new(CurrentDate(now).Value);

    /// <summary>
    /// Builds the Ctrl+; edit cell. A blank General cell receives the built-in short-date style in
    /// the same undoable edit as the serial; populated/formula cells and non-General formats keep
    /// their existing style.
    /// </summary>
    public static Cell CreateCurrentDateShortcutCell(Workbook workbook, CellAddress address, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheet = workbook.GetSheet(address.Sheet);
        var existingCell = sheet?.GetCell(address);
        var effectiveStyleId = existingCell?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        var cell = Cell.FromValue(CurrentDateSerial(now));

        // A value edit replaces a formula/value but must retain an explicit existing style.
        if (existingCell is not null)
            cell.StyleId = existingCell.StyleId;

        if (existingCell is { HasFormula: true } or { Value: not BlankValue } ||
            !string.Equals(
                workbook.GetStyle(effectiveStyleId).NumberFormat,
                CellStyle.Default.NumberFormat,
                StringComparison.OrdinalIgnoreCase))
        {
            return cell;
        }

        var style = workbook.GetStyle(effectiveStyleId).Clone();
        style.NumberFormat = CurrentDateNumberFormat;
        cell.StyleId = workbook.RegisterStyle(style);
        return cell;
    }

    public static DateTimeValue CurrentTime(DateTime now) =>
        new(now.TimeOfDay.TotalDays);
}
