using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class DateTimeEntryService
{
    /// <summary>
    /// Excel's built-in short-date format (numFmtId 14). The XLSX writer recognizes this canonical
    /// code and writes the built-in id instead of a custom number format.
    /// </summary>
    public const string CurrentDateNumberFormat = "m/d/yyyy";

    public static DateTimeValue CurrentDate(DateTime now) =>
        DateTimeValue.FromDateTime(now.Date);

    public static DateTimeValue CurrentTime(DateTime now) =>
        new(now.TimeOfDay.TotalDays);
}
