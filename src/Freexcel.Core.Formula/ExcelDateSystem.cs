namespace Freexcel.Core.Formula;

internal static class ExcelDateSystem
{
    private static readonly DateTime OleAutomationEpoch = new(1899, 12, 30);
    private static readonly DateTime FakeLeapDayBoundary = new(1900, 3, 1);

    public static DateTime SerialToDate(double serial) =>
        OleAutomationEpoch.AddDays(serial < 60 ? serial + 1 : serial);

    public static double DateToSerial(DateTime date)
    {
        var serial = (date - OleAutomationEpoch).TotalDays;
        return date < FakeLeapDayBoundary ? serial - 1 : serial;
    }
}
