namespace FreeX.Core.Formula;

internal static class ExcelDateSystem
{
    private static readonly DateTime OleAutomationEpoch = new(1899, 12, 30);
    private static readonly DateTime Date1904Epoch = new(1904, 1, 1);
    private static readonly DateTime FakeLeapDayBoundary = new(1900, 3, 1);
    private static readonly double Max1900Serial = DateToSerial(DateTime.MaxValue.Date);
    private static readonly double Max1904Serial = (DateTime.MaxValue.Date - Date1904Epoch).TotalDays;

    public static DateTime SerialToDate(double serial) =>
        OleAutomationEpoch.AddDays(serial < 60 ? serial + 1 : serial);

    public static DateTime SerialToDate(double serial, bool uses1904DateSystem) =>
        uses1904DateSystem ? Date1904Epoch.AddDays(serial) : SerialToDate(serial);

    public static bool TrySerialToDate(double serial, bool uses1904DateSystem, out DateTime date)
    {
        date = default;
        if (!double.IsFinite(serial) || serial < 0)
            return false;

        var maxSerial = uses1904DateSystem ? Max1904Serial : Max1900Serial;
        if (serial > maxSerial)
            return false;

        try
        {
            date = SerialToDate(serial, uses1904DateSystem);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static double DateToSerial(DateTime date)
    {
        var serial = (date - OleAutomationEpoch).TotalDays;
        return date < FakeLeapDayBoundary ? serial - 1 : serial;
    }

    public static double DateToSerial(DateTime date, bool uses1904DateSystem) =>
        uses1904DateSystem ? (date - Date1904Epoch).TotalDays : DateToSerial(date);

    /// <summary>
    /// True when <paramref name="serial"/> is Excel's phantom 1900 leap day (serial 60,
    /// i.e. "1900-02-29"). That day does not exist in the real Gregorian calendar, so
    /// .NET's <see cref="DateTime"/> cannot represent it: <see cref="SerialToDate(double)"/>
    /// maps both serial 59 ("1900-02-28") and serial 60 onto the same DateTime value
    /// (1900-02-28), which is otherwise indistinguishable from a genuine collision.
    /// Callers that need to detect this specific, Excel-only edge case (rather than
    /// silently treating it as 1900-02-28) can check this first.
    /// </summary>
    public static bool IsPhantomLeapDaySerial(double serial) => serial == 60;

    /// <summary>
    /// Returns the day-count difference between two Excel serials, computed directly in
    /// serial space (<paramref name="endSerial"/> - <paramref name="startSerial"/>) rather
    /// than by converting both serials to <see cref="DateTime"/> via
    /// <see cref="SerialToDate(double)"/> and subtracting those.
    /// </summary>
    /// <remarks>
    /// Excel's serial numbering already accounts for the phantom 1900-02-29 leap day for
    /// every serial &gt;= 61, so plain serial subtraction reproduces Excel's own day-count
    /// semantics across the 59/60/61 boundary. Converting through <see cref="DateTime"/>
    /// first does not: because serials 59 and 60 both map to 1900-02-28, a difference such
    /// as serial 61 minus serial 59 (which Excel treats as a 2-day span) would come out as
    /// only 1 day if computed via DateTime subtraction. Prefer this helper -- or equivalent
    /// direct serial arithmetic -- over DateTime subtraction for any day-count calculation
    /// that might span the 1900 phantom leap day.
    /// </remarks>
    public static double SerialDayDifference(double startSerial, double endSerial) => endSerial - startSerial;
}
