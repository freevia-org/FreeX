using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class DateTimeEntryService
{
    /// <summary>
    /// Excel's built-in short-date format (numFmtId 14). The XLSX writer recognizes this canonical
    /// code and writes the built-in id instead of a custom number format.
    /// </summary>
    public const string CurrentDateNumberFormat = "m/d/yy";

    public static DateTimeValue CurrentDate(DateTime now) =>
        DateTimeValue.FromDateTime(now.Date);

    /// <summary>
    /// Creates the value written by Ctrl+;. Excel stores dates as numeric serials; the caller
    /// applies <see cref="CurrentDateNumberFormat"/> when the target is a General-format blank.
    /// </summary>
    public static NumberValue CurrentDateSerial(DateTime now) =>
        new(CurrentDate(now).Value);

    /// <summary>
    /// Builds the Ctrl+; cell edit. A blank cell carrying General (including a General-formatted
    /// row/column default) receives Excel's short-date format in the same undoable edit as the
    /// serial value. Existing values, formulas, and non-General formats retain their formatting.
    /// </summary>
    public static Cell CreateCurrentDateShortcutCell(Workbook workbook, CellAddress address, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var cell = Cell.FromValue(CurrentDateSerial(now));
        var sheet = workbook.GetSheet(address.Sheet);
        var existingCell = sheet?.GetCell(address);
        if (existingCell is { HasFormula: true } or { Value: not BlankValue })
            return cell;

        var styleId = existingCell?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        if (!string.Equals(
                workbook.GetStyle(styleId).NumberFormat,
                CellStyle.Default.NumberFormat,
                StringComparison.OrdinalIgnoreCase))
        {
            return cell;
        }

        var style = workbook.GetStyle(styleId).Clone();
        style.NumberFormat = CurrentDateNumberFormat;
        cell.StyleId = workbook.RegisterStyle(style);
        return cell;
    }

    public static DateTimeValue CurrentTime(DateTime now) =>
        new(now.TimeOfDay.TotalDays);
}
