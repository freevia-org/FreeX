using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxFilterValueTextFormatter
{
    public static string ToFilterText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => ToDateFilterText(dateTime),
        ErrorValue error => error.Code,
        _ => string.Empty
    };

    private static string ToDateFilterText(DateTimeValue dateTime)
    {
        try
        {
            return dateTime.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentException)
        {
            return dateTime.Value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
