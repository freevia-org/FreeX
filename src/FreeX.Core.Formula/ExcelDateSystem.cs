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
}
